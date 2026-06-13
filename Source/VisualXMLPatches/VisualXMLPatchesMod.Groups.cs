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
}
