using System;
using Verse;

namespace VisualXMLPatches;

internal partial class VisualXMLPatchesMod
{
    // Filtered records are grouped once per dirty search instead of using LINQ grouping during every IMGUI draw event.

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

        foreach (var record in filteredRecords)
        {
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
            if (!record.Failed)
            {
                continue;
            }

            group.FailedCount++;
            group.HasFailure = true;
        }

        groupedRecords.Sort(CompareGroups);
        foreach (var patchGroupView in groupedRecords)
        {
            patchGroupView.Collapsed =
                getOrAssignDefaultCollapsed(patchGroupView.Key, patchGroupView.HasFailure);
        }
    }

    private static int CompareGroups(PatchGroupView a, PatchGroupView b)
    {
        var loadCompare = a.LoadOrderIndex.CompareTo(b.LoadOrderIndex);
        return loadCompare != 0
            ? loadCompare
            : string.Compare(a.ModName, b.ModName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetModKey(ModContentPack mod)
    {
        return mod == null ? "<unknown>" : $"{mod.PackageIdPlayerFacing}|{mod.Name}|{mod.RootDir}";
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
}