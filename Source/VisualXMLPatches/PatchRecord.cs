using Verse;

namespace VisualXMLPatches;

// Immutable-in-practice view model for one applied PatchOperation.
//
// The original settings window repeatedly reflected fields, built display strings,
// and formatted XML while Unity's IMGUI was drawing. With large mod lists that made
// an idle window and each typed character expensive. PatchRecord stores the cheap,
// stable metadata once so the UI can render/search cached data instead.
internal sealed class PatchRecord
{
    public int Index;
    public PatchOperation Patch;
    public ModContentPack Mod;

    public string ModName = string.Empty;
    public string PackageId = string.Empty;
    public int LoadOrderIndex = int.MaxValue;

    public bool Success;
    public bool Failed;

    public string PatchTypeFull = string.Empty;
    public string PatchTypeDisplay = string.Empty;
    public string XPath = string.Empty;
    public string SourceFile = string.Empty;
    public string Attribute = string.Empty;
    public string ModsSummary = string.Empty;
    public string OperationsSummary = string.Empty;
    public string DisplayXPath = string.Empty;

    // Cached search haystack for cheap fields. Patch values are not included here on
    // purpose: extracting/pretty-printing values can parse XML and should not happen
    // during ordinary typing unless a future explicit "search values" mode is added.
    public string SearchText = string.Empty;

    public bool HasValueField;

    // Value formatting is lazy. These fields are filled the first time the row is
    // expanded, then reused on every repaint/layout pass afterwards.
    public bool ValueComputed;
    public string FormattedValue = string.Empty;

    public bool HasDetails => !string.IsNullOrEmpty(Attribute) || HasValueField;
}
