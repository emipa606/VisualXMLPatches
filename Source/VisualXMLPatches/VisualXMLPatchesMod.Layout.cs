using UnityEngine;
using Verse;

namespace VisualXMLPatches;

internal partial class VisualXMLPatchesMod
{
    // Scroll height and visibility helpers. Layout measurement is kept separate from drawing so future row-height caching can be reasoned about in one place.

    private static float CalculateTotalHeight(float detailsWidth)
    {
        // Height calculation still has to walk the logical rows so the scrollbar is
        // correct, but it no longer recomputes unused details. The old code measured
        // mods/operations detail text that was not actually drawn, so that work was
        // removed rather than cached.
        var totalHeight = 0f;
        for (var g = 0; g < groupedRecords.Count; g++)
        {
            var group = groupedRecords[g];
            totalHeight += HeaderHeight;
            if (group.Collapsed)
            {
                continue;
            }

            for (var r = 0; r < group.Records.Count; r++)
            {
                var record = group.Records[r];
                totalHeight += RowHeight;
                if (expandedPatches.Contains(record.Index) && record.HasDetails)
                {
                    totalHeight += CalculateDetailHeight(record, detailsWidth);
                }
            }
        }

        return totalHeight;
    }

    private static float CalculateDetailHeight(PatchRecord record, float detailsWidth)
    {
        var height = 0f;
        if (!string.IsNullOrEmpty(record.Attribute))
        {
            height += calcValueHeight($"attribute: {record.Attribute}", detailsWidth) + 4f;
        }

        if (record.HasValueField)
        {
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
