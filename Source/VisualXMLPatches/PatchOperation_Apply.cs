using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace VisualXMLPatches;

[HarmonyPatch(typeof(PatchOperation), nameof(PatchOperation.Apply))]
public static class PatchOperation_Apply
{
    public static void Prefix(PatchOperation __instance)
    {
        VisualXMLPatches.Patches ??= [];
        VisualXMLPatches.Patches.Add(__instance);
        VisualXMLPatches.Mods.Add(VisualXMLPatches.PatchToMod.GetValueOrDefault(__instance));
    }
}