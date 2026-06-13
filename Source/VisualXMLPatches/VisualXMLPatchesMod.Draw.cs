using System;
using UnityEngine;
using Verse;

namespace VisualXMLPatches;

internal partial class VisualXMLPatchesMod
{
    // Drawing helpers only. Keep data discovery, searching and XML extraction out of this file so IMGUI repaint/layout events stay cheap.

    private void DrawTopControls(Rect topRect)
    {
        // Keep all secondary controls on one row. Listing_Standard is useful for
        // vertical forms, but direct rects give better alignment beside the count
        // label and the Collapse All button.
        var controlsY = topRect.y + 66f;
        var collapseRect = new Rect(topRect.xMax - CollapseButtonWidth, controlsY, CollapseButtonWidth,
            TopControlHeight);
        var toggleLabel = "VXP.IncludeXmlValues".Translate().ToString();
        // CheckboxLabeled draws the label at the left of its rect and the check box
        // at the right. Size the rect to the text instead of using a broad fixed
        // area so the label stays visually connected to the check mark.
        var toggleWidth = Text.CalcSize(toggleLabel).x + ValueToggleLabelGap + ValueToggleCheckboxSize;
        var toggleRect = new Rect(collapseRect.x - toggleWidth - TopControlGap, controlsY, toggleWidth,
            TopControlHeight);
        var countRect = new Rect(topRect.x, controlsY, Math.Max(160f, toggleRect.x - topRect.x - TopControlGap),
            TopControlHeight);

        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(countRect, "VXP.FoundPatches".Translate($"{filteredRecords.Count}/{patchRecords.Count}"));
        Text.Anchor = TextAnchor.UpperLeft;

        var previousIncludeXmlValues = includeXmlValues;
        Widgets.CheckboxLabeled(toggleRect, toggleLabel, ref includeXmlValues);
        TooltipHandler.TipRegion(toggleRect, "VXP.IncludeXmlValuesTooltip".Translate());

        if (previousIncludeXmlValues != includeXmlValues)
        {
            OnIncludeXmlValuesChanged();
        }

        if (Widgets.ButtonText(collapseRect, "VXP.CollapseAll".Translate()))
        {
            CollapseAllVisible();
        }

        TooltipHandler.TipRegion(collapseRect, "VXP.CollapseAllTooltip".Translate());
    }

    private void OnIncludeXmlValuesChanged()
    {
        settings ??= GetSettings<VisualXMLPatchesSettings>();
        settings.IncludeXmlValues = includeXmlValues;
        WriteSettings();

        // The query text did not necessarily change, but the searchable fields did.
        // Apply any pending text immediately so explicit toggle clicks are not held
        // behind the typing debounce.
        pendingSearchQuery = (searchFilter ?? string.Empty).Trim();
        ApplyPendingSearchQuery();
        lastAppliedSearchQuery = null;
        filterDirty = true;
        groupsDirty = true;
    }

    private static void CollapseAllVisible()
    {
        expandedPatches.Clear();

        // Collapse only groups in the current filtered view, but clear expanded rows
        // globally so hidden expanded details do not reappear when the search changes.
        for (var i = 0; i < groupedRecords.Count; i++)
        {
            var group = groupedRecords[i];
            group.Collapsed = true;
            collapsedPerMod[group.Key] = true;
        }
    }

    private static void DrawGroupHeader(PatchGroupView group, Rect headerRect)
    {
        if (Mouse.IsOver(headerRect))
        {
            Widgets.DrawHighlight(headerRect);
        }

        if (group.HasFailure)
        {
            Widgets.DrawBoxSolid(headerRect, new Color(0.4f, 0f, 0f, 0.18f));
        }

        var iconRect = new Rect(headerRect.x + 4f, headerRect.y + ((HeaderHeight - IconSize) / 2f), IconSize,
            IconSize);
        var previewTex = group.Mod?.ModMetaData?.PreviewImage;
        if (previewTex != null)
        {
            GUI.DrawTexture(iconRect, previewTex, ScaleMode.ScaleToFit);
        }
        else
        {
            Widgets.DrawBoxSolid(iconRect, new Color(0f, 0f, 0f, 0.3f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(iconRect, group.Mod == null ? "(unknown)" : "no img");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        Text.Anchor = TextAnchor.MiddleLeft;
        var failSuffix = group.FailedCount > 0 ? $"  !{group.FailedCount}" : string.Empty;
        var labelRect = new Rect(iconRect.xMax + 6f, headerRect.y, headerRect.width - (iconRect.xMax + 6f),
            HeaderHeight);
        var toggleLabel = $"{(group.Collapsed ? "+" : "-")} {group.ModName} ({group.Count}){failSuffix}";
        if (Widgets.ButtonInvisible(headerRect))
        {
            group.Collapsed = !group.Collapsed;
            collapsedPerMod[group.Key] = group.Collapsed;
        }

        if (group.HasFailure)
        {
            GUI.color = ColorLibrary.RedReadable;
        }

        Widgets.Label(labelRect, toggleLabel);
        if (group.HasFailure)
        {
            GUI.color = Color.white;
        }

        TooltipHandler.TipRegion(headerRect,
            group.PackageId + (group.HasFailure ? $"\nFailed patches: {group.FailedCount}" : string.Empty));
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void DrawPatchRow(PatchRecord record, Rect rowRect, ref bool expanded)
    {
        var marker = record.HasDetails ? expanded ? "-" : "+" : " ";
        var label = $"{marker} {record.RowText}";

        if (Mouse.IsOver(rowRect))
        {
            Widgets.DrawHighlight(rowRect);
        }

        if (record.Failed)
        {
            Widgets.DrawBoxSolid(rowRect, new Color(0.4f, 0f, 0f, 0.15f));
        }

        var openRect = new Rect(rowRect.xMax - OpenWidth, rowRect.y + ((rowRect.height - OpenButtonHeight) / 2f),
            OpenWidth - 4f, OpenButtonHeight);
        if (!string.IsNullOrEmpty(record.SourceFile))
        {
            if (Widgets.ButtonText(openRect, "VXP.Open".Translate()))
            {
                openSourceFile(record.SourceFile, record.Mod);
            }

            TooltipHandler.TipRegion(openRect, record.SourceFile);
        }

        var labelRectRow = new Rect(rowRect.x + RowHorizontalPadding, rowRect.y + 2f, GetPatchRowTextWidth(rowRect.width),
            Math.Max(1f, rowRect.height - 4f));
        Text.Anchor = TextAnchor.MiddleLeft;
        if (record.HasDetails && Widgets.ButtonInvisible(labelRectRow))
        {
            if (!expandedPatches.Add(record.Index))
            {
                expandedPatches.Remove(record.Index);
                expanded = false;
            }
            else
            {
                expanded = true;
            }
        }

        if (record.Failed)
        {
            GUI.color = ColorLibrary.RedReadable;
        }

        var oldWrap = Text.WordWrap;
        Text.WordWrap = true;
        Widgets.Label(labelRectRow, label);
        Text.WordWrap = oldWrap;
        if (record.Failed)
        {
            GUI.color = Color.white;
        }

        TooltipHandler.TipRegion(labelRectRow, record.RowTooltip);
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void DrawPatchDetails(PatchRecord record, ref float curY, float detailsWidth, float visibleTop,
        float visibleBottom)
    {
        // Details are the only place where patch values are fetched/formatted. This
        // keeps collapsed rows cheap and makes the user pay the XML formatting cost
        // only for rows they intentionally inspect.
        if (!string.IsNullOrEmpty(record.Attribute))
        {
            DrawDetailBlockIfVisible(ref curY, detailsWidth, $"attribute: {record.Attribute}",
                new Color(0.15f, 0.15f, 0.15f, 0.25f), visibleTop, visibleBottom);
        }

        if (!record.HasValueField)
        {
            return;
        }

        if (!includeXmlValues)
        {
            DrawDetailBlockIfVisible(ref curY, detailsWidth, "VXP.XmlValueHidden".Translate(),
                new Color(0.2f, 0.2f, 0.2f, 0.18f), visibleTop, visibleBottom, 8f);
            return;
        }

        var value = GetFormattedValue(record);
        if (!string.IsNullOrEmpty(value))
        {
            DrawDetailBlockIfVisible(ref curY, detailsWidth, value.Trim(), new Color(0.2f, 0.2f, 0.2f, 0.25f),
                visibleTop, visibleBottom, 8f);
        }
    }

    private static void DrawDetailBlockIfVisible(ref float curY, float width, string text, Color bg, float visibleTop,
        float visibleBottom, float extraBottomPadding = 4f)
    {
        var h = calcValueHeight(text, width);
        if (IsVisible(curY, h + extraBottomPadding, visibleTop, visibleBottom))
        {
            var valueRect = new Rect(DetailIndent, curY, width, h);
            Widgets.DrawBoxSolid(new Rect(valueRect.x - 4f, valueRect.y - 2f, valueRect.width + 8f,
                valueRect.height + 4f), bg);
            var oldWrap = Text.WordWrap;
            Text.WordWrap = true;
            Text.Font = GameFont.Tiny;
            Widgets.Label(valueRect, text);
            Text.Font = GameFont.Small;
            Text.WordWrap = oldWrap;
        }

        curY += h + extraBottomPadding;
    }

    private static void DrawVersion(Rect rect)
    {
        if (string.IsNullOrEmpty(currentVersion))
        {
            return;
        }

        var verRect = new Rect(rect.x, rect.yMax - 18f, rect.width, 18f);
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(verRect, "VXP.ModVersion".Translate(currentVersion));
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }
}
