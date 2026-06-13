using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using HarmonyLib;
using Mlie;
using RimWorld;
using UnityEngine;
using Verse;

namespace VisualXMLPatches;

[StaticConstructorOnStartup]
internal partial class VisualXMLPatchesMod : Mod
{
    // This class is split into partial files by responsibility. The main file keeps
    // the public Mod entry points and shared state; indexing, search, layout, drawing
    // and XML value handling live beside it in focused partials.
    // IMGUI methods are called repeatedly for layout, repaint and input events.
    // Keep DoSettingsWindowContents as close to a pure draw method as possible:
    // build patch metadata once, rebuild search/groups only when dirty, and defer
    // expensive XML formatting until a row is actually expanded.
    private const float IconSize = 32f;
    private const float TopAreaHeight = 112f;
    private const float HeaderHeight = 40f;
    private const float RowHeight = 32f;
    private const float OpenWidth = 60f;
    private const float OpenButtonHeight = 24f;
    private const float OpenButtonGap = 8f;
    private const float RowHorizontalPadding = 4f;
    private const float RowVerticalPadding = 6f;
    private const float DetailIndent = 24f;
    private const float DetailRightPadding = OpenWidth + 20f;
    private const float CollapseButtonWidth = 110f;
    private const float ValueToggleWidth = 280f;
    private const float TopControlHeight = 32f;
    private const float TopControlGap = 12f;
    private const float SearchDebounceSeconds = 0.25f;
    private const int MaxPatchRowLines = 3;
    private const int MinXmlValueSearchLength = 2;
    private static string currentVersion;
    private static VisualXMLPatchesSettings settings;
    private static Vector2 patchesScrollPosition;
    private static string searchFilter = string.Empty;

    // UI state. Sets/dictionaries are used only for membership/state lookups;
    // ordered patch display is still driven by List<PatchRecord> so applied patch
    // order remains stable and visible.
    private static readonly Dictionary<string, bool> collapsedPerMod = new();
    private static readonly HashSet<int> expandedPatches = [];
    private static readonly Dictionary<(Type type, string field), FieldInfo> fieldCache = new();
    private static readonly Dictionary<object, string> xmlFormatCache = new();

    // Cached view models. The old implementation assembled anonymous rows, grouped
    // them with LINQ, and reflected patch fields inside the draw loop. These lists
    // move that work to explicit rebuild points instead of every IMGUI event.
    private static readonly List<PatchRecord> patchRecords = [];
    private static readonly List<PatchRecord> filteredRecords = [];
    private static readonly List<PatchGroupView> groupedRecords = [];
    private static readonly Dictionary<string, PatchGroupView> groupBuildMap = new();

    // Dirty flags describe which derived views must be rebuilt. Search and grouping
    // are intentionally separate so typing does not force patch metadata extraction.
    private static Dictionary<ModContentPack, int> loadOrderMap;
    private static int loadOrderCount = -1;
    private static int indexedPatchCount = -1;
    private static int indexedResultCount = -1;
    private static bool indexDirty = true;
    private static bool filterDirty = true;
    private static bool groupsDirty = true;
    private static string lastAppliedSearchQuery;
    private static bool refreshedPatchDiscoveryForUi;
    private static string pendingSearchQuery = string.Empty;
    private static string appliedSearchQuery = string.Empty;
    private static float lastSearchEditTime = -1f;
    private static bool includeXmlValues;
    private static float cachedRowLineHeight = -1f;
    private static float cachedAverageRowCharacterWidth = -1f;

    // XmlWriterSettings allocation used to happen as part of value formatting.
    // Keep one settings instance because formatting may still run for expanded rows.
    private static readonly XmlWriterSettings PrettyXmlSettings = new()
    {
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
        OmitXmlDeclaration = true,
        ConformanceLevel = ConformanceLevel.Fragment
    };

    public VisualXMLPatchesMod(ModContentPack content) : base(content)
    {
        new Harmony("Mlie.VisualXMLPatches").PatchAll(Assembly.GetExecutingAssembly());
        currentVersion = VersionFromManifest.GetVersionFromModMetaData(content.ModMetaData);
        settings = GetSettings<VisualXMLPatchesSettings>();
        includeXmlValues = settings.IncludeXmlValues;
    }

    public override string SettingsCategory()
    {
        return "Visual XML Patches";
    }

    public override void DoSettingsWindowContents(Rect rect)
    {
        // All expensive data preparation below is guarded. With large mod lists this
        // prevents an idle settings window from repeatedly redoing search/group work.
        EnsurePatchIndex();

        var topRect = new Rect(rect.x, rect.y, rect.width, TopAreaHeight);
        var lowerRect = new Rect(rect.x, rect.y + TopAreaHeight + 6f, rect.width, rect.height - TopAreaHeight - 6f);

        var searchLabelRect = new Rect(topRect.x, topRect.y, topRect.width, 24f);
        Widgets.Label(searchLabelRect,
            includeXmlValues ? "VXP.SearchWithXmlValues".Translate() : "VXP.SearchWithoutXmlValues".Translate());

        var searchRect = new Rect(topRect.x, topRect.y + 28f, topRect.width, 30f);
        searchFilter = Widgets.TextField(searchRect, searchFilter ?? string.Empty);
        var query = (searchFilter ?? string.Empty).Trim();
        var activeQuery = GetDebouncedSearchQuery(query);

        // Filtering uses the debounced/applied query, not the currently typed text.
        // The textbox remains responsive while search waits for the typing pause.
        EnsureFilteredRecords(activeQuery);
        EnsureGroups();

        DrawTopControls(topRect);

        var outRect = lowerRect;
        var viewWidth = outRect.width - 16f;
        // Detail blocks sit under patch rows, but should not extend under the
        // right-side Open buttons. Use the same width for height calculation and
        // drawing so wrapped details cannot overlap later rows.
        var detailsWidth = Math.Max(120f, viewWidth - DetailIndent - DetailRightPadding);
        var totalHeightCalc = CalculateTotalHeight(detailsWidth, viewWidth);
        var viewRect = new Rect(0f, 0f, viewWidth, Math.Max(totalHeightCalc + 10f, outRect.height - 1f));
        // The scroll view may contain tens of thousands of rows. We still advance
        // curY for layout correctness, but only draw controls that intersect the
        // visible viewport.
        var visibleTop = patchesScrollPosition.y;
        var visibleBottom = patchesScrollPosition.y + outRect.height;

        Widgets.BeginScrollView(outRect, ref patchesScrollPosition, viewRect);
        var curY = 0f;

        foreach (var group in groupedRecords)
        {
            var headerRect = new Rect(0f, curY, viewRect.width, HeaderHeight);
            if (IsVisible(curY, HeaderHeight, visibleTop, visibleBottom))
            {
                DrawGroupHeader(group, headerRect);
            }

            curY += HeaderHeight;
            if (group.Collapsed)
            {
                continue;
            }

            for (var i = 0; i < group.Records.Count; i++)
            {
                var record = group.Records[i];
                var expanded = expandedPatches.Contains(record.Index);
                var rowWidth = GetPatchRowWidth(record, viewRect.width);
                var rowTextWidth = GetPatchRowTextWidth(rowWidth);
                var rowHeight = GetPatchRowHeight(record, rowTextWidth);
                var rowRect = new Rect(8f, curY, rowWidth, rowHeight);
                if (string.IsNullOrEmpty(record.SourceFile))
                {
                    rowRect.x += 10f;
                }

                if (IsVisible(curY, rowHeight, visibleTop, visibleBottom))
                {
                    DrawPatchRow(record, rowRect, ref expanded);
                }

                curY += rowHeight;
                if (!expanded || !record.HasDetails)
                {
                    continue;
                }

                DrawPatchDetails(record, ref curY, detailsWidth, visibleTop, visibleBottom);
            }
        }

        Widgets.EndScrollView();
        DrawVersion(rect);
    }


}
