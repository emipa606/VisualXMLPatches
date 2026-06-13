using System.Collections.Generic;
using Verse;

namespace VisualXMLPatches;

// Cached group for one owning mod in the current filtered view.
//
// This replaces per-frame LINQ grouping/sorting/counting. Groups are rebuilt only
// when the applied search query changes or the patch index changes. The Records list
// preserves the original patch application order inside each mod group.
internal sealed class PatchGroupView
{
    public readonly List<PatchRecord> Records = [];
    public bool Collapsed;

    public int Count;
    public int FailedCount;
    public bool HasFailure;
    public string Key = string.Empty;
    public int LoadOrderIndex = int.MaxValue;
    public ModContentPack Mod;
    public string ModName = string.Empty;
    public string PackageId = string.Empty;
}