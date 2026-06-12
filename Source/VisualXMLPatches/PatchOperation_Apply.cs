using HarmonyLib;
using Verse;

namespace VisualXMLPatches;

[HarmonyPatch(typeof(PatchOperation), nameof(PatchOperation.Apply))]
public static class PatchOperation_Apply
{
    public static void Prefix(PatchOperation __instance, out int __state)
    {
        // Prefix records the application order before Verse executes the patch. The
        // returned index is stored as Harmony state so the postfix can write the bool
        // result back to the same row without searching for the patch again.
        __state = VisualXMLPatches.RecordPatchStart(__instance);
    }

    public static void Postfix(bool __result, int __state)
    {
        // The old UI inferred failure later from private neverSucceeded state. Recording
        // the Apply return value here is both cheaper and more direct, while the UI still
        // keeps a defensive fallback if Results ever gets out of sync.
        VisualXMLPatches.RecordPatchResult(__state, __result);
    }
}
