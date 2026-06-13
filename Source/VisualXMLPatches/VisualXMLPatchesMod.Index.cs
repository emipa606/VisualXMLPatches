using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace VisualXMLPatches;

internal partial class VisualXMLPatchesMod
{
    // Patch discovery and PatchRecord indexing. Reflection belongs here or in Xml/Reflection helpers, not in the draw loop.

    private static void EnsurePatchIndex()
    {
        // This replaces the old per-frame row construction. Patch metadata changes
        // only when patches/results are captured, so the index can stay stable across
        // layout/repaint/input events.
        EnsureCompletePatchCapture();
        EnsureResultsAligned();

        var patchCount = VisualXMLPatches.Patches?.Count ?? 0;
        var resultCount = VisualXMLPatches.Results?.Count ?? 0;
        if (!indexDirty && indexedPatchCount == patchCount && indexedResultCount == resultCount)
        {
            return;
        }

        RebuildPatchIndex();
    }

    private static void EnsureCompletePatchCapture()
    {
        if (!refreshedPatchDiscoveryForUi)
        {
            // Refresh ownership/discovery after startup. In normal load orders the
            // Harmony prefix has already captured every Apply call, but if the mod
            // is constructed late it may only see a small tail of patches. The
            // discovery fallback keeps the settings window complete instead of
            // showing only those late captures.
            VisualXMLPatches.RebuildPatchDiscovery();
            refreshedPatchDiscoveryForUi = true;
        }

        if (!VisualXMLPatches.EnsureCapturedPatchListComplete())
        {
            return;
        }

        indexDirty = true;
        filterDirty = true;
        groupsDirty = true;
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
            var patch = VisualXMLPatches.Patches?[i];
            var success = VisualXMLPatches.Results != null && VisualXMLPatches.Results[i];
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
        var displayXPath = xpath == "VXP.NoXPath".Translate()
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
            ModName = mod?.Name ?? "VXP.UnknownMod".Translate(),
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
            DisplayXPathSingleLine = normalizeSingleLine(displayXPath),
            HasValueField = patch != null && hasPatchValueField(patch)
        };

        record.RowText = BuildPatchRowText(record);
        record.RowTooltip = BuildPatchRowTooltip(record);
        record.SearchText = BuildSearchText(record);
        return record;
    }

    private static int GetLoadOrderIndex(Dictionary<ModContentPack, int> loadOrder, ModContentPack mod)
    {
        if (mod == null || loadOrder == null)
        {
            return int.MaxValue;
        }

        return loadOrder.GetValueOrDefault(mod, int.MaxValue);
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
}