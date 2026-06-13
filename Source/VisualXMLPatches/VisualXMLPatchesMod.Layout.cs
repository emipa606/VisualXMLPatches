using System;
using Verse;

namespace VisualXMLPatches;

internal partial class VisualXMLPatchesMod
{
    // Scroll height and visibility helpers. Layout measurement is kept separate from drawing so future row-height caching can be reasoned about in one place.

    private static float CalculateTotalHeight(float detailsWidth, float viewWidth)
    {
        // Height calculation still has to walk the logical rows so the scrollbar is
        // correct. Patch row heights are cached per display width, which keeps
        // bounded multiline rows from becoming a repeated IMGUI-layout cost.
        var totalHeight = 0f;
        foreach (var group in groupedRecords)
        {
            totalHeight += HeaderHeight;
            if (group.Collapsed)
            {
                continue;
            }

            foreach (var record in group.Records)
            {
                var rowWidth = GetPatchRowWidth(record, viewWidth);
                var rowTextWidth = GetPatchRowTextWidth(rowWidth);
                totalHeight += GetPatchRowHeight(record, rowTextWidth);
                if (expandedPatches.Contains(record.Index) && record.HasDetails)
                {
                    totalHeight += CalculateDetailHeight(record, detailsWidth);
                }
            }
        }

        return totalHeight;
    }

    private static float GetPatchRowWidth(PatchRecord record, float viewWidth)
    {
        var width = viewWidth - 8f;
        if (string.IsNullOrEmpty(record.SourceFile))
        {
            width -= 10f;
        }

        return Math.Max(120f, width);
    }

    private static float GetPatchRowTextWidth(float rowWidth)
    {
        // Reserve a stable right-side column for the Open button even if the
        // current row has no source file. That keeps labels aligned and prevents
        // wrapped text from running under buttons on mixed rows.
        return Math.Max(80f, rowWidth - OpenWidth - OpenButtonGap - (RowHorizontalPadding * 2f));
    }

    private static float GetPatchRowHeight(PatchRecord record, float textWidth)
    {
        textWidth = Math.Max(80f, textWidth);
        if (record.CachedRowHeight > 0f && Math.Abs(record.CachedRowTextWidth - textWidth) < 0.5f)
        {
            return record.CachedRowHeight;
        }

        // Use a cheap estimated wrap count rather than measuring every row with
        // Text.CalcHeight on each full-list height pass. The estimate intentionally
        // errs slightly high so long technical paths get room to breathe, then caps
        // at MaxPatchRowLines to keep the list predictable with huge patch counts.
        var labelLength = Math.Max(1, record.RowText.Length + 2);
        var averageCharacterWidth = Math.Max(5f, GetAverageRowCharacterWidth() * 1.15f);
        var charactersPerLine = Math.Max(20, (int)(textWidth / averageCharacterWidth));
        var estimatedLines = Math.Max(1, (labelLength + charactersPerLine - 1) / charactersPerLine);
        estimatedLines = Math.Min(MaxPatchRowLines, estimatedLines);

        var rowHeight = Math.Max(RowHeight, (GetRowLineHeight() * estimatedLines) + RowVerticalPadding);
        record.CachedRowTextWidth = textWidth;
        record.CachedRowHeight = rowHeight;
        return rowHeight;
    }

    private static float GetRowLineHeight()
    {
        if (cachedRowLineHeight > 0f)
        {
            return cachedRowLineHeight;
        }

        var oldFont = Text.Font;
        var oldWrap = Text.WordWrap;
        Text.Font = GameFont.Small;
        Text.WordWrap = false;
        cachedRowLineHeight = Math.Max(16f, Text.CalcHeight("Ag", 1000f));
        Text.WordWrap = oldWrap;
        Text.Font = oldFont;
        return cachedRowLineHeight;
    }

    private static float GetAverageRowCharacterWidth()
    {
        if (cachedAverageRowCharacterWidth > 0f)
        {
            return cachedAverageRowCharacterWidth;
        }

        const string sample = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789/_[]().";
        var oldFont = Text.Font;
        var oldWrap = Text.WordWrap;
        Text.Font = GameFont.Small;
        Text.WordWrap = false;
        cachedAverageRowCharacterWidth = Math.Max(5f, Text.CalcSize(sample).x / sample.Length);
        Text.WordWrap = oldWrap;
        Text.Font = oldFont;
        return cachedAverageRowCharacterWidth;
    }

    private static float CalculateDetailHeight(PatchRecord record, float detailsWidth)
    {
        var height = 0f;
        if (!string.IsNullOrEmpty(record.Attribute))
        {
            height += calcValueHeight($"attribute: {record.Attribute}", detailsWidth) + 4f;
        }

        if (!record.HasValueField)
        {
            return height;
        }

        if (includeXmlValues)
        {
            var value = GetFormattedValue(record);
            if (!string.IsNullOrEmpty(value))
            {
                height += calcValueHeight(value.Trim(), detailsWidth) + 8f;
            }
        }
        else
        {
            height += calcValueHeight("VXP.XmlValueHidden".Translate(), detailsWidth) + 8f;
        }

        return height;
    }

    private static bool IsVisible(float y, float height, float visibleTop, float visibleBottom)
    {
        return y + height >= visibleTop && y <= visibleBottom;
    }

    private static float calcValueHeight(string value, float width)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0f;
        }

        var oldWrap = Text.WordWrap;
        Text.WordWrap = true;
        Text.Font = GameFont.Tiny;
        var h = Text.CalcHeight(value.Trim(), width);
        Text.Font = GameFont.Small;
        Text.WordWrap = oldWrap;
        return h;
    }
}