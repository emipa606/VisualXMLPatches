using Verse;

namespace VisualXMLPatches;

internal sealed class VisualXMLPatchesSettings : ModSettings
{
    // Default off: large mod lists stay fast unless the user explicitly needs XML
    // value display/search. RimWorld saves this through normal ModSettings Scribe.
    public bool IncludeXmlValues;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref IncludeXmlValues, "includeXmlValues");
        base.ExposeData();
    }
}