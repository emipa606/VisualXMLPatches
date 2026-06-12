using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using HarmonyLib;
using Mlie;
using RimWorld;
using UnityEngine;
using Verse;

namespace VisualXMLPatches;

[StaticConstructorOnStartup]
internal class VisualXMLPatchesMod : Mod
{
    // IMGUI methods are called repeatedly for layout, repaint and input events.
    // Keep DoSettingsWindowContents as close to a pure draw method as possible:
    // build patch metadata once, rebuild search/groups only when dirty, and defer
    // expensive XML formatting until a row is actually expanded.
    private const float IconSize = 32f;
    private const float TopAreaHeight = 82f;
    private const float HeaderHeight = 40f;
    private const float RowHeight = 32f;
    private const float OpenWidth = 60f;
    private const float SearchDebounceSeconds = 0.25f;
    private static string currentVersion;
    private static Vector2 patchesScrollPosition;
    private static string searchFilter = string.Empty;

    // UI state. Sets/dictionaries are used only for membership/state lookups;
    // ordered patch display is still driven by List<PatchRecord> so applied patch
    // order remains stable and visible.
    private static readonly Dictionary<string, bool> collapsedPerMod = new();
    private static readonly HashSet<int> expandedPatches = [];
    private static readonly Dictionary<(Type type, string field), FieldInfo> fieldCache = new();
    private static readonly Dictionary<object, string> xmlFormatCache = new();

    // Cached view models. The old implementation assembled anonymous rows, grouped
    // them with LINQ, and reflected patch fields inside the draw loop. These lists
    // move that work to explicit rebuild points instead of every IMGUI event.
    private static readonly List<PatchRecord> patchRecords = [];
    private static readonly List<PatchRecord> filteredRecords = [];
    private static readonly List<PatchGroupView> groupedRecords = [];
    private static readonly Dictionary<string, PatchGroupView> groupBuildMap = new();

    // Dirty flags describe which derived views must be rebuilt. Search and grouping
    // are intentionally separate so typing does not force patch metadata extraction.
    private static Dictionary<ModContentPack, int> loadOrderMap;
    private static int loadOrderCount = -1;
    private static int indexedPatchCount = -1;
    private static int indexedResultCount = -1;
    private static bool indexDirty = true;
    private static bool filterDirty = true;
    private static bool groupsDirty = true;
    private static string lastAppliedSearchQuery;
    private static string pendingSearchQuery = string.Empty;
    private static string appliedSearchQuery = string.Empty;
    private static float lastSearchEditTime = -1f;

    // XmlWriterSettings allocation used to happen as part of value formatting.
    // Keep one settings instance because formatting may still run for expanded rows.
    private static readonly XmlWriterSettings PrettyXmlSettings = new()
    {
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
        OmitXmlDeclaration = true,
        ConformanceLevel = ConformanceLevel.Fragment
    };

    public VisualXMLPatchesMod(ModContentPack content) : base(content)
    {
        new Harmony("Mlie.VisualXMLPatches").PatchAll(Assembly.GetExecutingAssembly());
        currentVersion = VersionFromManifest.GetVersionFromModMetaData(content.ModMetaData);
    }

    public override string SettingsCategory()
    {
        return "Visual XML Patches";
    }

    public override void DoSettingsWindowContents(Rect rect)
    {
        // All expensive data preparation below is guarded. With large mod lists this
        // prevents an idle settings window from repeatedly redoing search/group work.
        EnsurePatchIndex();

        var topRect = new Rect(rect.x, rect.y, rect.width, TopAreaHeight);
        var lowerRect = new Rect(rect.x, rect.y + TopAreaHeight + 6f, rect.width, rect.height - TopAreaHeight - 6f);

        var listingTop = new Listing_Standard { ColumnWidth = topRect.width };
        listingTop.Begin(topRect);
        listingTop.Label("VXP.Search".Translate());
        searchFilter = listingTop.TextEntry(searchFilter ?? string.Empty);
        var query = (searchFilter ?? string.Empty).Trim();
        var activeQuery = GetDebouncedSearchQuery(query);

        // Filtering uses the debounced/applied query, not the currently typed text.
        // The textbox remains responsive while search waits for the typing pause.
        EnsureFilteredRecords(activeQuery);
        EnsureGroups();

        listingTop.Label("VXP.FoundPatches".Translate($"{filteredRecords.Count}/{patchRecords.Count}"));
        listingTop.End();

        var outRect = lowerRect;
        var viewWidth = outRect.width - 16f;
        var detailsWidth = viewWidth - 70f;
        var totalHeightCalc = CalculateTotalHeight(detailsWidth);
        var viewRect = new Rect(0f, 0f, viewWidth, Math.Max(totalHeightCalc + 10f, outRect.height - 1f));
        // The scroll view may contain tens of thousands of rows. We still advance
        // curY for layout correctness, but only draw controls that intersect the
        // visible viewport.
        var visibleTop = patchesScrollPosition.y;
        var visibleBottom = patchesScrollPosition.y + outRect.height;

        Widgets.BeginScrollView(outRect, ref patchesScrollPosition, viewRect);
        var curY = 0f;

        foreach (var group in groupedRecords)
        {
            var headerRect = new Rect(0f, curY, viewRect.width, HeaderHeight);
            if (IsVisible(curY, HeaderHeight, visibleTop, visibleBottom))
            {
                DrawGroupHeader(group, headerRect);
            }

            curY += HeaderHeight;
            if (group.Collapsed)
            {
                continue;
            }

            for (var i = 0; i < group.Records.Count; i++)
            {
                var record = group.Records[i];
                var expanded = expandedPatches.Contains(record.Index);
                var rowRect = new Rect(8f, curY, viewRect.width - 8f, RowHeight);
                if (string.IsNullOrEmpty(record.SourceFile))
                {
                    rowRect.x += 10f;
                    rowRect.width -= 10f;
                }

                if (IsVisible(curY, RowHeight, visibleTop, visibleBottom))
                {
                    DrawPatchRow(record, rowRect, ref expanded);
                }

                curY += RowHeight;
                if (!expanded || !record.HasDetails)
                {
                    continue;
                }

                DrawPatchDetails(record, ref curY, detailsWidth, visibleTop, visibleBottom);
            }
        }

        Widgets.EndScrollView();
        DrawVersion(rect);
    }

    private static string GetDebouncedSearchQuery(string query)
    {
        // Debounce by design rather than necessity: even though cached search is fast,
        // there is no value in rebuilding a 30k+ patch result set for every character
        // while the user is still typing. Clearing search applies immediately because
        // users expect the full list to return without delay.
        query ??= string.Empty;
        var now = Time.realtimeSinceStartup;

        if (!string.Equals(query, pendingSearchQuery, StringComparison.Ordinal))
        {
            pendingSearchQuery = query;
            lastSearchEditTime = now;

            if (string.IsNullOrEmpty(query))
            {
                ApplyPendingSearchQuery();
            }
        }

        if (!string.Equals(pendingSearchQuery, appliedSearchQuery, StringComparison.Ordinal) &&
            now - lastSearchEditTime >= SearchDebounceSeconds)
        {
            ApplyPendingSearchQuery();
        }

        return appliedSearchQuery;
    }

    private static void ApplyPendingSearchQuery()
    {
        if (string.Equals(appliedSearchQuery, pendingSearchQuery, StringComparison.Ordinal))
        {
            return;
        }

        appliedSearchQuery = pendingSearchQuery;
        filterDirty = true;
    }

    private static void EnsurePatchIndex()
    {
        // This replaces the old per-frame row construction. Patch metadata changes
        // only when patches/results are captured, so the index can stay stable across
        // layout/repaint/input events.
        EnsureResultsAligned();

        var patchCount = VisualXMLPatches.Patches?.Count ?? 0;
        var resultCount = VisualXMLPatches.Results?.Count ?? 0;
        if (!indexDirty && indexedPatchCount == patchCount && indexedResultCount == resultCount)
        {
            return;
        }

        RebuildPatchIndex();
    }

    private static void EnsureResultsAligned()
    {
        // Defensive fallback for older captured state or failed Harmony result capture.
        // Normal flow records success in PatchOperation_Apply.Postfix, but this keeps
        // the UI usable even if Results gets out of sync with Patches.
        VisualXMLPatches.Patches ??= [];
        VisualXMLPatches.Results ??= [];

        if (VisualXMLPatches.Results.Count == VisualXMLPatches.Patches.Count)
        {
            return;
        }

        VisualXMLPatches.Results.Clear();
        foreach (var patchOperation in VisualXMLPatches.Patches)
        {
            VisualXMLPatches.Results.Add(!getNeverSucceeded(patchOperation));
        }
    }

    private static void RebuildPatchIndex()
    {
        // Extract cheap, frequently displayed/searchable fields once. Do not format
        // the value field here: value may contain XML, and formatting it was one of
        // the main sources of typing/idle lag in the original render-loop approach.
        patchRecords.Clear();
        var loadOrder = GetLoadOrderMap();
        var count = Math.Min(VisualXMLPatches.Patches?.Count ?? 0, VisualXMLPatches.Results?.Count ?? 0);
        if (patchRecords.Capacity < count)
        {
            patchRecords.Capacity = count;
        }

        for (var i = 0; i < count; i++)
        {
            var patch = VisualXMLPatches.Patches[i];
            var success = VisualXMLPatches.Results[i];
            var record = BuildPatchRecord(i, patch, success, loadOrder);
            patchRecords.Add(record);
        }

        indexedPatchCount = VisualXMLPatches.Patches?.Count ?? 0;
        indexedResultCount = VisualXMLPatches.Results?.Count ?? 0;
        indexDirty = false;
        filterDirty = true;
        groupsDirty = true;
    }

    private static PatchRecord BuildPatchRecord(int index, PatchOperation patch, bool success,
        Dictionary<ModContentPack, int> loadOrder)
    {
        // Build a stable snapshot of the patch for display/search. Reflection remains
        // necessary because Verse patch operations keep useful fields private, but it
        // should happen during indexing, not every time Unity asks the window to draw.
        var mod = resolveModForIndex(index);
        var patchTypeFull = patch?.GetType().Name ?? string.Empty;
        var patchTypeDisplay = patchTypeFull.StartsWith("PatchOperation", StringComparison.Ordinal)
            ? patchTypeFull["PatchOperation".Length..]
            : patchTypeFull;
        var xpath = patch == null ? string.Empty : getPatchXPath(patch);
        var sourceFile = patch == null ? string.Empty : getPatchSourceFile(patch);
        var attribute = patch == null ? string.Empty : getPatchAttribute(patch);
        var modsSummary = patch == null ? string.Empty : getPatchMods(patch);
        var operationsSummary = patch == null ? string.Empty : getPatchOperationsSummary(patch);
        var displayXPath = xpath == "(no xpath)"
            ? !string.IsNullOrEmpty(operationsSummary)
                ? operationsSummary
                : !string.IsNullOrEmpty(modsSummary)
                    ? modsSummary
                    : xpath
            : xpath;

        var record = new PatchRecord
        {
            Index = index,
            Patch = patch,
            Mod = mod,
            ModName = mod?.Name ?? "<Unknown Mod>",
            PackageId = mod?.PackageIdPlayerFacing ?? string.Empty,
            LoadOrderIndex = GetLoadOrderIndex(loadOrder, mod),
            Success = success,
            Failed = !success,
            PatchTypeFull = patchTypeFull,
            PatchTypeDisplay = patchTypeDisplay,
            XPath = xpath,
            SourceFile = sourceFile,
            Attribute = attribute,
            ModsSummary = modsSummary,
            OperationsSummary = operationsSummary,
            DisplayXPath = displayXPath,
            HasValueField = patch != null && hasPatchValueField(patch)
        };

        record.SearchText = BuildSearchText(record);
        return record;
    }


    private static int GetLoadOrderIndex(Dictionary<ModContentPack, int> loadOrder, ModContentPack mod)
    {
        if (mod == null || loadOrder == null)
        {
            return int.MaxValue;
        }

        return loadOrder.TryGetValue(mod, out var index) ? index : int.MaxValue;
    }

    private static string BuildSearchText(PatchRecord record)
    {
        // One cached haystack per row avoids repeated ToLowerInvariant allocations
        // and repeated field-by-field checks. Patch values are deliberately excluded
        // from default search because they can require XML parsing/pretty-printing.
        var sb = new StringBuilder();
        AppendSearchPart(sb, record.ModName);
        AppendSearchPart(sb, record.PackageId);
        AppendSearchPart(sb, record.PatchTypeFull);
        AppendSearchPart(sb, record.PatchTypeDisplay);
        AppendSearchPart(sb, record.XPath);
        AppendSearchPart(sb, record.SourceFile);
        AppendSearchPart(sb, record.Attribute);
        AppendSearchPart(sb, record.ModsSummary);
        AppendSearchPart(sb, record.OperationsSummary);
        AppendSearchPart(sb, record.Success ? "success succeeded applied" : "fail failed error");
        return sb.ToString();
    }

    private static void AppendSearchPart(StringBuilder sb, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append('\n');
        }

        sb.Append(value);
    }

    private static void EnsureFilteredRecords(string query)
    {
        // Rebuild only when the applied query changes. The original code effectively
        // searched on every IMGUI event, so one typed character could trigger multiple
        // full scans before the next character was entered.
        EnsurePatchIndex();

        if (!filterDirty && string.Equals(query, lastAppliedSearchQuery, StringComparison.Ordinal))
        {
            return;
        }

        filteredRecords.Clear();
        if (filteredRecords.Capacity < patchRecords.Count)
        {
            filteredRecords.Capacity = patchRecords.Count;
        }

        if (string.IsNullOrEmpty(query))
        {
            filteredRecords.AddRange(patchRecords);
        }
        else
        {
            for (var i = 0; i < patchRecords.Count; i++)
            {
                var record = patchRecords[i];
                if (ContainsIgnoreCase(record.SearchText, query))
                {
                    filteredRecords.Add(record);
                }
            }
        }

        lastAppliedSearchQuery = query;
        filterDirty = false;
        groupsDirty = true;
    }

    private static void EnsureGroups()
    {
        if (!groupsDirty)
        {
            return;
        }

        RebuildGroups();
        groupsDirty = false;
    }

    private static void RebuildGroups()
    {
        // Grouping replaces the old GroupBy/OrderBy/Any/Count pipeline in the draw
        // method. Counts and failure flags are precomputed here so rendering headers
        // is simple and allocation-free.
        groupedRecords.Clear();
        groupBuildMap.Clear();

        for (var i = 0; i < filteredRecords.Count; i++)
        {
            var record = filteredRecords[i];
            var key = GetModKey(record.Mod);
            if (!groupBuildMap.TryGetValue(key, out var group))
            {
                group = new PatchGroupView
                {
                    Mod = record.Mod,
                    Key = key,
                    ModName = record.ModName,
                    PackageId = record.PackageId,
                    LoadOrderIndex = record.LoadOrderIndex
                };
                groupBuildMap[key] = group;
                groupedRecords.Add(group);
            }

            group.Records.Add(record);
            group.Count++;
            if (record.Failed)
            {
                group.FailedCount++;
                group.HasFailure = true;
            }
        }

        groupedRecords.Sort(CompareGroups);
        for (var i = 0; i < groupedRecords.Count; i++)
        {
            groupedRecords[i].Collapsed = getOrAssignDefaultCollapsed(groupedRecords[i].Key, groupedRecords[i].HasFailure);
        }
    }

    private static int CompareGroups(PatchGroupView a, PatchGroupView b)
    {
        var loadCompare = a.LoadOrderIndex.CompareTo(b.LoadOrderIndex);
        if (loadCompare != 0)
        {
            return loadCompare;
        }

        return string.Compare(a.ModName, b.ModName, StringComparison.OrdinalIgnoreCase);
    }

    private static float CalculateTotalHeight(float detailsWidth)
    {
        // Height calculation still has to walk the logical rows so the scrollbar is
        // correct, but it no longer recomputes unused details. The old code measured
        // mods/operations detail text that was not actually drawn, so that work was
        // removed rather than cached.
        var totalHeight = 0f;
        for (var g = 0; g < groupedRecords.Count; g++)
        {
            var group = groupedRecords[g];
            totalHeight += HeaderHeight;
            if (group.Collapsed)
            {
                continue;
            }

            for (var r = 0; r < group.Records.Count; r++)
            {
                var record = group.Records[r];
                totalHeight += RowHeight;
                if (expandedPatches.Contains(record.Index) && record.HasDetails)
                {
                    totalHeight += CalculateDetailHeight(record, detailsWidth);
                }
            }
        }

        return totalHeight;
    }

    private static float CalculateDetailHeight(PatchRecord record, float detailsWidth)
    {
        var height = 0f;
        if (!string.IsNullOrEmpty(record.Attribute))
        {
            height += calcValueHeight($"attribute: {record.Attribute}", detailsWidth) + 4f;
        }

        if (record.HasValueField)
        {
            var value = GetFormattedValue(record);
            if (!string.IsNullOrEmpty(value))
            {
                height += calcValueHeight(value.Trim(), detailsWidth) + 8f;
            }
        }

        return height;
    }

    private static void DrawGroupHeader(PatchGroupView group, Rect headerRect)
    {
        if (Mouse.IsOver(headerRect))
        {
            Widgets.DrawHighlight(headerRect);
        }

        if (group.HasFailure)
        {
            Widgets.DrawBoxSolid(headerRect, new Color(0.4f, 0f, 0f, 0.18f));
        }

        var iconRect = new Rect(headerRect.x + 4f, headerRect.y + ((HeaderHeight - IconSize) / 2f), IconSize,
            IconSize);
        var previewTex = group.Mod?.ModMetaData?.PreviewImage;
        if (previewTex != null)
        {
            GUI.DrawTexture(iconRect, previewTex, ScaleMode.ScaleToFit);
        }
        else
        {
            Widgets.DrawBoxSolid(iconRect, new Color(0f, 0f, 0f, 0.3f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(iconRect, group.Mod == null ? "(unknown)" : "no img");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        Text.Anchor = TextAnchor.MiddleLeft;
        var failSuffix = group.FailedCount > 0 ? $"  !{group.FailedCount}" : string.Empty;
        var labelRect = new Rect(iconRect.xMax + 6f, headerRect.y, headerRect.width - (iconRect.xMax + 6f),
            HeaderHeight);
        var toggleLabel = $"{(group.Collapsed ? "+" : "-")} {group.ModName} ({group.Count}){failSuffix}";
        if (Widgets.ButtonInvisible(headerRect))
        {
            group.Collapsed = !group.Collapsed;
            collapsedPerMod[group.Key] = group.Collapsed;
        }

        if (group.HasFailure)
        {
            GUI.color = ColorLibrary.RedReadable;
        }

        Widgets.Label(labelRect, toggleLabel);
        if (group.HasFailure)
        {
            GUI.color = Color.white;
        }

        TooltipHandler.TipRegion(headerRect,
            group.PackageId + (group.HasFailure ? $"\nFailed patches: {group.FailedCount}" : string.Empty));
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void DrawPatchRow(PatchRecord record, Rect rowRect, ref bool expanded)
    {
        var displayIndex = record.Index + 1;
        var marker = record.HasDetails ? expanded ? "-" : "+" : " ";
        var statusTag = record.Success ? string.Empty : " [FAIL]";
        var label = $"{marker} #{displayIndex}: {record.PatchTypeDisplay}{statusTag} | {shorten(record.DisplayXPath, 80)}";

        if (Mouse.IsOver(rowRect))
        {
            Widgets.DrawHighlight(rowRect);
        }

        if (record.Failed)
        {
            Widgets.DrawBoxSolid(rowRect, new Color(0.4f, 0f, 0f, 0.15f));
        }

        var openRect = new Rect(rowRect.xMax - OpenWidth, rowRect.y + 4f, OpenWidth - 4f, RowHeight - 8f);
        if (!string.IsNullOrEmpty(record.SourceFile))
        {
            if (Widgets.ButtonText(openRect, "VXP.Open".Translate()))
            {
                openSourceFile(record.SourceFile, record.Mod);
            }

            TooltipHandler.TipRegion(openRect, record.SourceFile);
        }

        var labelRectRow = new Rect(rowRect.x + 4f, rowRect.y, rowRect.width - OpenWidth - 4f, RowHeight);
        Text.Anchor = TextAnchor.MiddleLeft;
        if (record.HasDetails && Widgets.ButtonInvisible(labelRectRow))
        {
            if (!expandedPatches.Add(record.Index))
            {
                expandedPatches.Remove(record.Index);
                expanded = false;
            }
            else
            {
                expanded = true;
            }
        }

        if (record.Failed)
        {
            GUI.color = ColorLibrary.RedReadable;
        }

        Widgets.Label(labelRectRow, label);
        if (record.Failed)
        {
            GUI.color = Color.white;
        }

        TooltipHandler.TipRegion(labelRectRow, record.DisplayXPath);
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void DrawPatchDetails(PatchRecord record, ref float curY, float detailsWidth, float visibleTop,
        float visibleBottom)
    {
        // Details are the only place where patch values are fetched/formatted. This
        // keeps collapsed rows cheap and makes the user pay the XML formatting cost
        // only for rows they intentionally inspect.
        if (!string.IsNullOrEmpty(record.Attribute))
        {
            DrawDetailBlockIfVisible(ref curY, detailsWidth, $"attribute: {record.Attribute}",
                new Color(0.15f, 0.15f, 0.15f, 0.25f), visibleTop, visibleBottom);
        }

        if (!record.HasValueField)
        {
            return;
        }

        var value = GetFormattedValue(record);
        if (!string.IsNullOrEmpty(value))
        {
            DrawDetailBlockIfVisible(ref curY, detailsWidth, value.Trim(), new Color(0.2f, 0.2f, 0.2f, 0.25f),
                visibleTop, visibleBottom, 8f);
        }
    }

    private static void DrawDetailBlockIfVisible(ref float curY, float width, string text, Color bg, float visibleTop,
        float visibleBottom, float extraBottomPadding = 4f)
    {
        var h = calcValueHeight(text, width);
        if (IsVisible(curY, h + extraBottomPadding, visibleTop, visibleBottom))
        {
            var valueRect = new Rect(24f, curY, width, h);
            Widgets.DrawBoxSolid(new Rect(valueRect.x - 4f, valueRect.y - 2f, valueRect.width + 8f,
                valueRect.height + 4f), bg);
            var oldWrap = Text.WordWrap;
            Text.WordWrap = true;
            Text.Font = GameFont.Tiny;
            Widgets.Label(valueRect, text);
            Text.Font = GameFont.Small;
            Text.WordWrap = oldWrap;
        }

        curY += h + extraBottomPadding;
    }

    private static bool IsVisible(float y, float height, float visibleTop, float visibleBottom)
    {
        return y + height >= visibleTop && y <= visibleBottom;
    }

    private static string GetFormattedValue(PatchRecord record)
    {
        // Lazy one-shot value formatting. Caching here is more important than caching
        // in getPatchValue because this is keyed by row/patch and survives redraws.
        if (record.ValueComputed)
        {
            return record.FormattedValue;
        }

        record.FormattedValue = record.Patch == null ? string.Empty : getPatchValue(record.Patch);
        record.ValueComputed = true;
        return record.FormattedValue;
    }

    private static Dictionary<ModContentPack, int> GetLoadOrderMap()
    {
        var mods = LoadedModManager.RunningModsListForReading;
        if (loadOrderMap != null && loadOrderCount == mods.Count)
        {
            return loadOrderMap;
        }

        loadOrderMap = new Dictionary<ModContentPack, int>(mods.Count);
        for (var i = 0; i < mods.Count; i++)
        {
            loadOrderMap[mods[i]] = i;
        }

        loadOrderCount = mods.Count;
        return loadOrderMap;
    }

    private static ModContentPack resolveModForIndex(int index)
    {
        // Prefer the mod captured at patch-apply time. Nested PatchOperationSequence
        // children may not carry their own direct mod entry, so fall back to the most
        // recent prior mod and finally to sourceFile/root-dir inference.
        if (index < VisualXMLPatches.Mods.Count)
        {
            var direct = VisualXMLPatches.Mods[index];
            if (direct == null)
            {
                for (var i = index - 1; i >= 0; i--)
                {
                    if (i >= VisualXMLPatches.Mods.Count)
                    {
                        continue;
                    }

                    var prev = VisualXMLPatches.Mods[i];
                    if (prev != null)
                    {
                        return prev;
                    }
                }
            }
            else
            {
                return direct;
            }
        }

        // Fallback: try to infer from sourceFile
        if (index >= VisualXMLPatches.Patches.Count)
        {
            return null;
        }

        var sf = getPatchSourceFile(VisualXMLPatches.Patches[index]);
        if (string.IsNullOrEmpty(sf))
        {
            return null;
        }

        foreach (var m in LoadedModManager.RunningMods)
        {
            try
            {
                if (!string.IsNullOrEmpty(m.RootDir) &&
                    sf.StartsWith(m.RootDir, StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }
            catch
            {
                // ignored
            }
        }

        return null;
    }

    private static string GetModKey(ModContentPack mod)
    {
        if (mod == null)
        {
            return "<unknown>";
        }

        return $"{mod.PackageIdPlayerFacing}|{mod.Name}|{mod.RootDir}";
    }

    private static bool getOrAssignDefaultCollapsed(string modKey, bool groupHasFailure)
    {
        if (collapsedPerMod.TryGetValue(modKey, out var collapsed))
        {
            return collapsed;
        }

        collapsed = !groupHasFailure;
        collapsedPerMod[modKey] = collapsed;
        return collapsed;
    }

    private static FieldInfo getFieldCached(Type t, string fieldName)
    {
        // Field lookup is cached separately from field value extraction. Values are
        // per patch and can change/contain different objects; FieldInfo is per type.
        var key = (t, fieldName);
        if (fieldCache.TryGetValue(key, out var fi))
        {
            return fi;
        }

        fi = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        fieldCache[key] = fi;
        return fi;
    }

    private static string getPatchXPath(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "xpath");
            if (fi?.GetValue(patch) is string s && !string.IsNullOrEmpty(s))
            {
                return s;
            }
        }
        catch
        {
            // ignored
        }

        return "(no xpath)";
    }

    private static bool getNeverSucceeded(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "neverSucceeded");
            if (fi?.GetValue(patch) is bool b)
            {
                return b;
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }

    private static bool hasPatchValueField(PatchOperation patch)
    {
        // Cheap existence check used for the expand marker. It intentionally does not
        // stringify or format the value.
        try
        {
            var fi = getFieldCached(patch.GetType(), "value");
            if (fi == null)
            {
                return false;
            }

            return fi.GetValue(patch) != null;
        }
        catch
        {
            return false;
        }
    }

    private static string getPatchValue(PatchOperation patch)
    {
        // Expensive path. Keep it out of indexing/search/drawing collapsed rows. XML
        // containers and nodes are formatted for readability only after expansion.
        try
        {
            var fi = getFieldCached(patch.GetType(), "value");
            if (fi == null)
            {
                return string.Empty;
            }

            var raw = fi.GetValue(patch);
            switch (raw)
            {
                case null:
                    return string.Empty;
                case string s:
                    return maybeFormatXmlString(s);
            }

            var rawType = raw.GetType();
            if (rawType.Name == "XmlContainer")
            {
                if (xmlFormatCache.TryGetValue(raw, out var cached))
                {
                    return cached;
                }

                var nodeField = getFieldCached(rawType, "node") ?? getFieldCached(rawType, "Node");
                if (nodeField?.GetValue(raw) is not XmlNode xn)
                {
                    return string.Empty;
                }

                var formatted = formatXmlNode(xn);
                xmlFormatCache[raw] = formatted;
                return formatted;
            }

            switch (raw)
            {
                case XmlNode xmlNode:
                    return formatXmlNode(xmlNode);
                case IEnumerable<XmlNode> nodeEnum:
                    return string.Join("\n", nodeEnum.Select(formatXmlNode));
            }

            var generic = raw.ToString();
            if (!string.IsNullOrEmpty(generic) && generic != rawType.FullName)
            {
                return maybeFormatXmlString(generic);
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private static string getPatchAttribute(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "attribute");
            if (fi?.GetValue(patch) is string s && !string.IsNullOrEmpty(s))
            {
                return s;
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private static string getPatchSourceFile(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "sourceFile");
            if (fi?.GetValue(patch) is string s && !string.IsNullOrEmpty(s))
            {
                return s.Replace('/', Path.DirectorySeparatorChar);
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private static string getPatchMods(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "mods");
            if (fi == null)
            {
                return string.Empty;
            }

            var raw = fi.GetValue(patch);
            switch (raw)
            {
                case null:
                    return string.Empty;
                case IEnumerable<object> list:
                {
                    var items = list.Select(o => o?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    return items.Count == 0 ? string.Empty : $"mods: {string.Join(", ", items)}";
                }
                default:
                {
                    var str = raw.ToString();
                    return string.IsNullOrEmpty(str) ? string.Empty : $"mods: {str}";
                }
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private static IEnumerable<PatchOperation> getSubOperations(PatchOperation patch)
    {
        var fi = getFieldCached(patch.GetType(), "operations");
        if (fi == null)
        {
            yield break;
        }

        var raw = fi.GetValue(patch);
        if (raw is not IEnumerable<PatchOperation> enumOps)
        {
            yield break;
        }

        foreach (var op in enumOps)
        {
            if (op != null)
            {
                yield return op;
            }
        }
    }

    private static string getPatchOperationsSummary(PatchOperation patch)
    {
        var ops = getSubOperations(patch).Take(10).ToList();
        return ops.Count == 0
            ? string.Empty
            : $"operations: {string.Join(", ", ops.Select(o => o.GetType().Name.Replace("PatchOperation", string.Empty)))}";
    }

    // The previous implementation also built a verbose operations detail block for
    // height calculation, but it was never drawn. That unused work was removed
    // instead of cached because caching dead UI data would only preserve the cost in
    // a different place. getPatchOperationsSummary remains because it is cheap, shown
    // in collapsed rows when xpath is absent, and included in search.

    private static void openSourceFile(string path, ModContentPack mod)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var resolved = path;
        if (!Path.IsPathRooted(resolved))
        {
            try
            {
                if (!string.IsNullOrEmpty(mod?.RootDir))
                {
                    resolved = Path.Combine(mod.RootDir, resolved);
                }
            }
            catch
            {
                // ignored
            }
        }

        if (!File.Exists(resolved))
        {
            Messages.Message($"File not found: {resolved}", MessageTypeDefOf.RejectInput, false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = resolved, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Warning($"[VisualXMLPatches]: Could not open file '{resolved}': {e.Message}");
        }
    }

    private static float calcValueHeight(string value, float width)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0f;
        }

        var oldWrap = Text.WordWrap;
        Text.WordWrap = true;
        Text.Font = GameFont.Tiny;
        var h = Text.CalcHeight(value.Trim(), width);
        Text.Font = GameFont.Small;
        Text.WordWrap = oldWrap;
        return h;
    }

    private static string shorten(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return $"{value[..(max - 3)]}...";
    }

    private static bool ContainsIgnoreCase(string text, string query)
    {
        // Avoid ToLowerInvariant/ToUpperInvariant in the hot search path; those allocate
        // a new string per row. OrdinalIgnoreCase gives a case-insensitive scan without
        // building lowercase copies.
        return !string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(query) &&
               text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string maybeFormatXmlString(string input)
    {
        // Fast reject non-XML-looking strings. This keeps plain text values from paying
        // XmlDocument parsing costs.
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        if (input.IndexOf('<') < 0 || input.IndexOf('>') < 0)
        {
            return input;
        }

        try
        {
            return formatXmlFragment(input);
        }
        catch
        {
            return input;
        }
    }

    private static string formatXmlNode(XmlNode node)
    {
        if (node == null)
        {
            return string.Empty;
        }

        try
        {
            using var sw = new StringWriter();
            using (var xw = XmlWriter.Create(sw, PrettyXmlSettings))
            {
                if (node is XmlDocument doc)
                {
                    doc.DocumentElement?.WriteTo(xw);
                }
                else
                {
                    node.WriteTo(xw);
                }
            }

            return sw.ToString().Trim();
        }
        catch
        {
            return node.OuterXml;
        }
    }

    private static string formatXmlFragment(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return string.Empty;
        }

        var wrapped = fragment;
        try
        {
            var tempDoc = new XmlDocument();
            try
            {
                tempDoc.LoadXml(fragment);
            }
            catch
            {
                wrapped = $"<root>{fragment}</root>";
                tempDoc.LoadXml(wrapped);
            }

            using var sw = new StringWriter();
            using (var xw = XmlWriter.Create(sw, PrettyXmlSettings))
            {
                if (wrapped == fragment)
                {
                    if (tempDoc.DocumentElement != null)
                    {
                        tempDoc.DocumentElement.WriteTo(xw);
                    }
                }
                else
                {
                    if (tempDoc.DocumentElement == null)
                    {
                        return sw.ToString().Trim();
                    }

                    foreach (XmlNode child in tempDoc.DocumentElement.ChildNodes)
                    {
                        child.WriteTo(xw);
                    }
                }
            }

            return sw.ToString().Trim();
        }
        catch
        {
            return fragment.Trim();
        }
    }

    private static void DrawVersion(Rect rect)
    {
        if (string.IsNullOrEmpty(currentVersion))
        {
            return;
        }

        var verRect = new Rect(rect.x, rect.yMax - 18f, rect.width, 18f);
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(verRect, "VXP.ModVersion".Translate(currentVersion));
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }
}
