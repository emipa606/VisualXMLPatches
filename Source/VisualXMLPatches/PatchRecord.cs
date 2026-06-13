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
    public string Attribute = string.Empty;
    public float CachedRowHeight = -1f;

    // Cached row height for the current settings window width. Wrapped row labels are
    // measured once per width so multiline display does not become a new hot path.
    public float CachedRowTextWidth = -1f;
    public string DisplayXPath = string.Empty;
    public string DisplayXPathSingleLine = string.Empty;
    public bool Failed;
    public string FormattedValue = string.Empty;

    public bool HasValueField;
    public int Index;
    public int LoadOrderIndex = int.MaxValue;
    public ModContentPack Mod;

    public string ModName = string.Empty;
    public string ModsSummary = string.Empty;
    public string OperationsSummary = string.Empty;
    public string PackageId = string.Empty;
    public PatchOperation Patch;
    public string PatchTypeDisplay = string.Empty;

    public string PatchTypeFull = string.Empty;
    public string RowText = string.Empty;
    public string RowTooltip = string.Empty;

    // Cached search haystack for cheap fields. Patch values are deliberately separate
    // so XML value search can stay opt-in without changing ordinary search cost.
    public string SearchText = string.Empty;
    public string SourceFile = string.Empty;

    public bool Success;
    public bool ValueComputed;
    public string ValueSearchText = string.Empty;

    // Value search and value display use separate caches. Search keeps raw XML/text
    // because pretty-printing is a display concern and adds avoidable work. Display
    // formatting remains lazy and only runs when expanded values are actually shown.
    public bool ValueSearchTextComputed;
    public string XPath = string.Empty;

    public bool HasDetails => !string.IsNullOrEmpty(Attribute) || HasValueField;
}