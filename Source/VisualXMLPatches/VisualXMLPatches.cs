using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace VisualXMLPatches;

[StaticConstructorOnStartup]
public class VisualXMLPatches
{
    public static List<PatchOperation> Patches;
    public static List<bool> Results = [];
    public static readonly List<ModContentPack> Mods = []; // aligned 1:1 with Patches as they are applied
    internal static readonly Dictionary<PatchOperation, ModContentPack> PatchToMod = new();

    // Ordered discovery fallback. The Harmony hook is still the preferred source
    // because it records actual Apply calls, but mod construction/load timing can
    // leave the hook with only late patches in some load orders. Rebuilding this
    // map from RunningMods after startup gives the UI a complete, stable fallback
    // and also repairs mod ownership for patches captured before the map was ready.
    internal static readonly List<PatchOperation> DiscoveredPatches = [];
    internal static readonly List<ModContentPack> DiscoveredMods = [];

    private static readonly FieldInfo operationsField = typeof(PatchOperation).GetField("operations",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    static VisualXMLPatches()
    {
        RebuildPatchDiscovery();
    }

    internal static void RebuildPatchDiscovery()
    {
        PatchToMod.Clear();
        DiscoveredPatches.Clear();
        DiscoveredMods.Clear();

        // Pre-map all patch operations (including nested ones) to their owning mod.
        // This is intentionally rebuildable: the static constructor can run during
        // patch application, while the settings window runs after the mod list has
        // settled and can refresh the map with complete data.
        foreach (var mod in LoadedModManager.RunningMods)
        {
            var top = mod.Patches.ToArray();
            if (!top.Any())
            {
                continue;
            }

            foreach (var patch in top)
            {
                mapPatchTree(mod, patch, []);
            }
        }
    }

    internal static bool EnsureCapturedPatchListComplete()
    {
        Patches ??= [];
        Results ??= [];

        if (DiscoveredPatches.Count == 0 || Patches.Count >= DiscoveredPatches.Count)
        {
            return false;
        }

        // If the live Harmony capture only saw a small tail of the patch process,
        // replace it with the full ordered discovery list. Results are cleared on
        // purpose so the UI recomputes success/failure from PatchOperation state.
        Patches.Clear();
        Mods.Clear();
        Results.Clear();

        Patches.AddRange(DiscoveredPatches);
        Mods.AddRange(DiscoveredMods);
        return true;
    }

    internal static int RecordPatchStart(PatchOperation patch)
    {
        Patches ??= [];
        Results ??= [];

        var index = Patches.Count;
        Patches.Add(patch);
        PatchToMod.TryGetValue(patch, out var mod);
        Mods.Add(mod);
        Results.Add(true);
        return index;
    }

    internal static void RecordPatchResult(int index, bool success)
    {
        if (Results == null || index < 0 || index >= Results.Count)
        {
            return;
        }

        Results[index] = success;
    }

    private static void mapPatchTree(ModContentPack mod, PatchOperation patch, HashSet<PatchOperation> visited)
    {
        if (patch == null || !visited.Add(patch))
        {
            return;
        }

        PatchToMod.TryAdd(patch, mod);
        DiscoveredPatches.Add(patch);
        DiscoveredMods.Add(mod);

        // Recurse into sub-operations if present.
        if (operationsField?.GetValue(patch) is IEnumerable<PatchOperation> enumerable)
        {
            foreach (var sub in enumerable)
            {
                mapPatchTree(mod, sub, visited);
            }
        }
        else if (operationsField?.GetValue(patch) is IList<PatchOperation> list)
        {
            foreach (var sub in list)
            {
                mapPatchTree(mod, sub, visited);
            }
        }
    }
}
