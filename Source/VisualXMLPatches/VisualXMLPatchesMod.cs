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
    private const float IconSize = 32f;
    private const float TopAreaHeight = 82f;
    private const float HeaderHeight = 40f;
    private const float RowHeight = 32f;
    private const float OpenWidth = 60f;
    private static string currentVersion;
    private static Vector2 patchesScrollPosition;
    private static string searchFilter = string.Empty;
    private static readonly Dictionary<ModContentPack, bool> collapsedPerMod = new();
    private static readonly HashSet<int> expandedPatches = [];
    private static readonly Dictionary<(Type type, string field), FieldInfo> fieldCache = new();
    private static readonly Dictionary<object, string> xmlFormatCache = new();

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
        VisualXMLPatches.Results ??= new List<bool>(VisualXMLPatches.Patches.Count);

        if (VisualXMLPatches.Results.Count != VisualXMLPatches.Patches.Count)
        {
            VisualXMLPatches.Results.Clear();
            foreach (var patchOperation in VisualXMLPatches.Patches)
            {
                VisualXMLPatches.Results.Add(!getNeverSucceeded(patchOperation));
            }
        }

        var topRect = new Rect(rect.x, rect.y, rect.width, TopAreaHeight);
        var lowerRect = new Rect(rect.x, rect.y + TopAreaHeight + 6f, rect.width, rect.height - TopAreaHeight - 6f);

        var listingTop = new Listing_Standard { ColumnWidth = topRect.width };
        listingTop.Begin(topRect);
        listingTop.Label("VXP.Search".Translate());
        searchFilter = listingTop.TextEntry(searchFilter ?? string.Empty).Trim();
        listingTop.Label("VXP.FoundPatches".Translate(VisualXMLPatches.Patches.Count));
        listingTop.End();

        var count = Math.Min(VisualXMLPatches.Patches.Count,
            VisualXMLPatches.Results.Count); // do NOT limit by Mods list
        IEnumerable<(int index, ModContentPack mod, PatchOperation patch, bool success)> data = Enumerable
            .Range(0, count)
            .Select(i => (i, resolveModForIndex(i), VisualXMLPatches.Patches[i], VisualXMLPatches.Results[i]));

        if (!string.IsNullOrEmpty(searchFilter))
        {
            var filterLower = searchFilter.ToLowerInvariant();
            data = data.Where(d => (d.mod?.Name ?? string.Empty).ToLowerInvariant().Contains(filterLower)
                                   || d.patch.GetType().Name.ToLowerInvariant().Contains(filterLower)
                                   || getPatchXPath(d.patch).ToLowerInvariant().Contains(filterLower)
                                   || getPatchSourceFile(d.patch).ToLowerInvariant().Contains(filterLower)
                                   || getPatchAttribute(d.patch).ToLowerInvariant().Contains(filterLower)
                                   || getPatchValue(d.patch).ToLowerInvariant().Contains(filterLower)
                                   || getPatchMods(d.patch).ToLowerInvariant().Contains(filterLower)
                                   || getPatchOperationsSummary(d.patch).ToLowerInvariant().Contains(filterLower));
        }

        var loadOrderMap = LoadedModManager.RunningModsListForReading.Select((m, i) => (m, i))
            .ToDictionary(t => t.m, t => t.i);

        // FIX: handle null mod keys explicitly to avoid ArgumentNullException when ordering
        var grouped = data.GroupBy(d => d.mod)
            .OrderBy(g =>
            {
                if (g.Key == null)
                {
                    return int.MaxValue; // unknown mods sorted last
                }

                return loadOrderMap.GetValueOrDefault(g.Key, int.MaxValue);
            })
            .ThenBy(g => g.Key?.Name)
            .ToArray();

        var totalHeightCalc = 0f;
        foreach (var group in grouped)
        {
            var groupHasFailure = group.Any(p => !p.success);
            var collapsed = getOrAssignDefaultCollapsed(group.Key, groupHasFailure);
            totalHeightCalc += HeaderHeight;
            if (collapsed)
            {
                continue;
            }

            foreach (var entry in group)
            {
                totalHeightCalc += RowHeight;
                if (!expandedPatches.Contains(entry.index))
                {
                    continue;
                }

                var detailsWidth = lowerRect.width - 70f;
                var attribute = getPatchAttribute(entry.patch);
                var value = getPatchValue(entry.patch);
                var mods = getPatchMods(entry.patch);
                var operations = getPatchOperationsDetail(entry.patch);
                if (!string.IsNullOrEmpty(attribute))
                {
                    totalHeightCalc += calcValueHeight($"attribute: {attribute}", detailsWidth) + 4f;
                }

                if (!string.IsNullOrEmpty(mods))
                {
                    totalHeightCalc += calcValueHeight(mods, detailsWidth) + 4f;
                }

                if (!string.IsNullOrEmpty(operations))
                {
                    totalHeightCalc += calcValueHeight(operations, detailsWidth) + 4f;
                }

                if (!string.IsNullOrEmpty(value))
                {
                    totalHeightCalc += calcValueHeight(value, detailsWidth) + 8f;
                }
            }
        }

        var outRect = lowerRect;
        var viewRect = new Rect(0f, 0f, outRect.width - 16f, Math.Max(totalHeightCalc + 10f, outRect.height - 1f));
        Widgets.BeginScrollView(outRect, ref patchesScrollPosition, viewRect);
        var curY = 0f;

        foreach (var group in grouped)
        {
            var groupHasFailure = group.Any(p => !p.success);
            var collapsed = getOrAssignDefaultCollapsed(group.Key, groupHasFailure);
            var headerRect = new Rect(0f, curY, viewRect.width, HeaderHeight);
            if (Mouse.IsOver(headerRect))
            {
                Widgets.DrawHighlight(headerRect);
            }

            if (groupHasFailure)
            {
                Widgets.DrawBoxSolid(headerRect, new Color(0.4f, 0f, 0f, 0.18f));
            }

            var iconRect = new Rect(headerRect.x + 4f, headerRect.y + ((HeaderHeight - IconSize) / 2f), IconSize,
                IconSize);
            var previewTex = group.Key?.ModMetaData?.PreviewImage;
            if (previewTex != null)
            {
                GUI.DrawTexture(iconRect, previewTex, ScaleMode.ScaleToFit);
            }
            else
            {
                Widgets.DrawBoxSolid(iconRect, new Color(0f, 0f, 0f, 0.3f));
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(iconRect, group.Key == null ? "(unknown)" : "no img");
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            var modName = group.Key?.Name ?? "<Unknown Mod>";
            var failCount = group.Count(p => !p.success);
            var failSuffix = failCount > 0 ? $"  !{failCount}" : string.Empty;
            var labelRect = new Rect(iconRect.xMax + 6f, headerRect.y, headerRect.width - (iconRect.xMax + 6f),
                HeaderHeight);
            var toggleLabel = $"{(collapsed ? "+" : "-")} {modName} ({group.Count()}){failSuffix}";
            if (group.Key != null && Widgets.ButtonInvisible(headerRect))
            {
                collapsedPerMod[group.Key] = !collapsed;
                collapsed = !collapsed;
            }

            if (groupHasFailure)
            {
                GUI.color = ColorLibrary.RedReadable;
            }

            Widgets.Label(labelRect, toggleLabel);
            if (groupHasFailure)
            {
                GUI.color = Color.white;
            }

            if (group.Key != null)
            {
                TooltipHandler.TipRegion(headerRect,
                    group.Key.PackageIdPlayerFacing +
                    (groupHasFailure ? $"\nFailed patches: {failCount}" : string.Empty));
            }

            Text.Anchor = TextAnchor.UpperLeft;
            curY += HeaderHeight;
            if (collapsed)
            {
                continue;
            }

            foreach (var entry in group)
            {
                var displayIndex = entry.index + 1;
                var expanded = expandedPatches.Contains(entry.index);
                var patchType = entry.patch.GetType().Name;
                if (patchType.StartsWith("PatchOperation"))
                {
                    patchType = patchType["PatchOperation".Length..];
                }

                var xpath = getPatchXPath(entry.patch);
                var sourceFile = getPatchSourceFile(entry.patch);
                var attribute = getPatchAttribute(entry.patch);
                var value = getPatchValue(entry.patch);
                var modsText = getPatchMods(entry.patch);
                var operationsText = getPatchOperationsSummary(entry.patch);
                // If no xpath, substitute operations or mods summary for display clarity
                var displayXpath = xpath == "(no xpath)"
                    ? !string.IsNullOrEmpty(operationsText) ? operationsText :
                    !string.IsNullOrEmpty(modsText) ? modsText : xpath
                    : xpath;
                var hasDetails = !string.IsNullOrEmpty(attribute) || !string.IsNullOrEmpty(value);
                var success = entry.success;
                var marker = hasDetails ? expanded ? "-" : "+" : " ";
                var statusTag = success ? string.Empty : " [FAIL]";
                var label = $"{marker} #{displayIndex}: {patchType}{statusTag} | {shorten(displayXpath, 80)}";

                var rowRect = new Rect(8f, curY, viewRect.width - 8f, RowHeight);
                if (string.IsNullOrEmpty(sourceFile))
                {
                    rowRect.x += 10;
                    rowRect.width -= 10f;
                }

                if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawHighlight(rowRect);
                }

                if (!success)
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.4f, 0f, 0f, 0.15f));
                }

                var openRect = new Rect(rowRect.xMax - OpenWidth, rowRect.y + 4f, OpenWidth - 4f, RowHeight - 8f);
                if (!string.IsNullOrEmpty(sourceFile))
                {
                    if (Widgets.ButtonText(openRect, "VXP.Open".Translate()))
                    {
                        openSourceFile(sourceFile, entry.mod);
                    }

                    TooltipHandler.TipRegion(openRect, sourceFile);
                }

                var labelRectRow = new Rect(rowRect.x + 4f, rowRect.y, rowRect.width - OpenWidth - 4f, RowHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                if (hasDetails && Widgets.ButtonInvisible(labelRectRow))
                {
                    if (!expandedPatches.Add(entry.index))
                    {
                        expandedPatches.Remove(entry.index);
                        expanded = false;
                    }
                }

                if (!success)
                {
                    GUI.color = ColorLibrary.RedReadable;
                }

                Widgets.Label(labelRectRow, label);
                if (!success)
                {
                    GUI.color = Color.white;
                }

                TooltipHandler.TipRegion(labelRectRow, displayXpath);
                Text.Anchor = TextAnchor.UpperLeft;

                curY += RowHeight;
                if (!expanded || !hasDetails)
                {
                    continue;
                }

                var detailsWidth = viewRect.width - 70f;
                if (!string.IsNullOrEmpty(attribute))
                {
                    drawDetailBlock(ref curY, detailsWidth, $"attribute: {attribute}",
                        new Color(0.15f, 0.15f, 0.15f, 0.25f));
                }

                if (!string.IsNullOrEmpty(value))
                {
                    drawDetailBlock(ref curY, detailsWidth, value.Trim(), new Color(0.2f, 0.2f, 0.2f, 0.25f), 8f);
                }
            }
        }

        Widgets.EndScrollView();

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

    private static void drawDetailBlock(ref float curY, float width, string text, Color bg,
        float extraBottomPadding = 4f)
    {
        var h = calcValueHeight(text, width);
        var valueRect = new Rect(24f, curY, width, h);
        Widgets.DrawBoxSolid(new Rect(valueRect.x - 4f, valueRect.y - 2f, valueRect.width + 8f, valueRect.height + 4f),
            bg);
        var oldWrap = Text.WordWrap;
        Text.WordWrap = true;
        Text.Font = GameFont.Tiny;
        Widgets.Label(valueRect, text);
        Text.Font = GameFont.Small;
        Text.WordWrap = oldWrap;
        curY += h + extraBottomPadding;
    }

    private static ModContentPack resolveModForIndex(int index)
    {
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

    private static bool getOrAssignDefaultCollapsed(ModContentPack mod, bool groupHasFailure)
    {
        if (mod == null)
        {
            return false;
        }

        if (collapsedPerMod.TryGetValue(mod, out var collapsed))
        {
            return collapsed;
        }

        collapsed = !groupHasFailure;
        collapsedPerMod[mod] = collapsed;
        return collapsed;
    }

    private static FieldInfo getFieldCached(Type t, string fieldName)
    {
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

    private static string getPatchValue(PatchOperation patch)
    {
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

    private static string getPatchOperationsDetail(PatchOperation patch)
    {
        var ops = getSubOperations(patch).ToList();
        if (ops.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("operations:");
        var idx = 1;
        foreach (var op in ops)
        {
            var type = op.GetType().Name.Replace("PatchOperation", string.Empty);
            var xp = getPatchXPath(op);
            sb.Append("  ").Append(idx++).Append(") ").Append(type);
            if (!string.IsNullOrEmpty(xp) && xp != "(no xpath)")
            {
                sb.Append(" | ").Append(shorten(xp, 80));
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

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

    private static string maybeFormatXmlString(string input)
    {
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
            if (node is XmlDocument doc)
            {
                return formatXmlFragment(doc.OuterXml);
            }

            return formatXmlFragment(node.OuterXml);
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

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = true
            };
            using var sw = new StringWriter();
            using (var xw = XmlWriter.Create(sw, settings))
            {
                if (wrapped == fragment)
                {
                    tempDoc.Save(xw);
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
}