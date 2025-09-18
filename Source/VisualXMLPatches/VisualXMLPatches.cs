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

    private static readonly FieldInfo operationsField = typeof(PatchOperation).GetField("operations",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    static VisualXMLPatches()
    {
        // Pre-map all patch operations (including nested ones) to their owning mod so that when they Apply we can record the correct mod.
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

    private static void mapPatchTree(ModContentPack mod, PatchOperation patch, HashSet<PatchOperation> visited)
    {
        if (patch == null || !visited.Add(patch))
        {
            return;
        }

        PatchToMod.TryAdd(patch, mod);

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