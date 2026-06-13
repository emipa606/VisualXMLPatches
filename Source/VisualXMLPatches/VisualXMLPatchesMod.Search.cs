using System;
using UnityEngine;

namespace VisualXMLPatches;

internal partial class VisualXMLPatchesMod
{
    // Search and debounce logic. Filtering reads cached PatchRecord search text and only touches XML values when the opt-in setting allows it.

    private static string GetDebouncedSearchQuery(string query)
    {
        // Debounce by design rather than necessity: even though cached search is fast,
        // there is no value in rebuilding a 30k+ patch result set for every character
        // while the user is still typing. Clearing search applies immediately because
        // users expect the full list to return without delay.
        query ??= string.Empty;
        var now = Time.realtimeSinceStartup;

        if (!string.Equals(query, pendingSearchQuery, StringComparison.Ordinal))
        {
            pendingSearchQuery = query;
            lastSearchEditTime = now;

            if (string.IsNullOrEmpty(query))
            {
                ApplyPendingSearchQuery();
            }
        }

        if (!string.Equals(pendingSearchQuery, appliedSearchQuery, StringComparison.Ordinal) &&
            now - lastSearchEditTime >= SearchDebounceSeconds)
        {
            ApplyPendingSearchQuery();
        }

        return appliedSearchQuery;
    }

    private static void ApplyPendingSearchQuery()
    {
        if (string.Equals(appliedSearchQuery, pendingSearchQuery, StringComparison.Ordinal))
        {
            return;
        }

        appliedSearchQuery = pendingSearchQuery;
        filterDirty = true;
    }

    private static void EnsureFilteredRecords(string query)
    {
        // Rebuild only when the applied query changes. The original code effectively
        // searched on every IMGUI event, so one typed character could trigger multiple
        // full scans before the next character was entered.
        EnsurePatchIndex();

        if (!filterDirty && string.Equals(query, lastAppliedSearchQuery, StringComparison.Ordinal))
        {
            return;
        }

        filteredRecords.Clear();
        if (filteredRecords.Capacity < patchRecords.Count)
        {
            filteredRecords.Capacity = patchRecords.Count;
        }

        if (string.IsNullOrEmpty(query))
        {
            filteredRecords.AddRange(patchRecords);
        }
        else
        {
            foreach (var record in patchRecords)
            {
                if (MatchesSearch(record, query))
                {
                    filteredRecords.Add(record);
                }
            }
        }

        lastAppliedSearchQuery = query;
        filterDirty = false;
        groupsDirty = true;
    }

    private static bool MatchesSearch(PatchRecord record, string query)
    {
        if (ContainsIgnoreCase(record.SearchText, query))
        {
            return true;
        }

        // XML value search is opt-in because extracting values can touch XML nodes
        // and create strings for many rows. Keep the cheap cached haystack first,
        // then fall back to lazy value text only when the user asked for it.
        return includeXmlValues && query.Length >= MinXmlValueSearchLength && record.HasValueField &&
               ContainsIgnoreCase(GetValueSearchText(record), query);
    }

    private static bool ContainsIgnoreCase(string text, string query)
    {
        // Avoid ToLowerInvariant/ToUpperInvariant in the hot search path; those allocate
        // a new string per row. OrdinalIgnoreCase gives a case-insensitive scan without
        // building lowercase copies.
        return !string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(query) &&
               text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}