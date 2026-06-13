using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using RimWorld;
using Verse;

namespace VisualXMLPatches;

internal partial class VisualXMLPatchesMod
{
    // Small utilities that do not own UI state: file opening, text normalization and compact display helpers.

    private static void openSourceFile(string path, ModContentPack mod)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var resolved = path;
        if (!Path.IsPathRooted(resolved))
        {
            try
            {
                if (!string.IsNullOrEmpty(mod?.RootDir))
                {
                    resolved = Path.Combine(mod.RootDir, resolved);
                }
            }
            catch
            {
                // ignored
            }
        }

        if (!File.Exists(resolved))
        {
            Messages.Message($"File not found: {resolved}", MessageTypeDefOf.RejectInput, false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = resolved, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Warning($"[VisualXMLPatches]: Could not open file '{resolved}': {e.Message}");
        }
    }

    private static string normalizeSingleLine(string value)
    {
        // Patch xpaths and sequence summaries can contain embedded newlines or
        // indentation. Normal rows are fixed-height for scrolling performance, so
        // keep row labels to one physical line and expose the full text by tooltip.
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasWhitespace)
                {
                    sb.Append(' ');
                    previousWasWhitespace = true;
                }
            }
            else
            {
                sb.Append(c);
                previousWasWhitespace = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static string BuildPatchRowText(PatchRecord record)
    {
        // Row text is a display string, not the exact xpath. Long xpaths often
        // contain slash/bracket separators rather than spaces, so add harmless
        // visual break points after common separators. The tooltip keeps the exact
        // unmodified xpath/summary for copyable inspection.
        var displayIndex = record.Index + 1;
        var statusTag = record.Success ? string.Empty : " [FAIL]";
        var displayPath = addRowWrapBreaks(record.DisplayXPathSingleLine);
        return $"#{displayIndex}: {record.PatchTypeDisplay}{statusTag} | {displayPath}";
    }

    private static string BuildPatchRowTooltip(PatchRecord record)
    {
        return string.IsNullOrEmpty(record.DisplayXPath) ? record.RowText : record.DisplayXPath;
    }

    private static string addRowWrapBreaks(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 16);
        var previousWasWhitespace = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasWhitespace)
                {
                    sb.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            sb.Append(c);
            previousWasWhitespace = false;

            if (c is '/' or ']' or ')' or ',' or ';' or '>')
            {
                sb.Append(' ');
                previousWasWhitespace = true;
            }
        }

        return sb.ToString().Trim();
    }

    private static string shorten(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return $"{value[..(max - 3)]}...";
    }
}
