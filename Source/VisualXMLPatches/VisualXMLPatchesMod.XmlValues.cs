using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Verse;

namespace VisualXMLPatches;

internal partial class VisualXMLPatchesMod
{
    // XML value extraction and formatting. These helpers are intentionally isolated because they are among the most expensive operations in the settings UI.

    private static string GetFormattedValue(PatchRecord record)
    {
        // Lazy one-shot value formatting. Caching here is more important than caching
        // in getPatchValue because this is keyed by row/patch and survives redraws.
        if (record.ValueComputed)
        {
            return record.FormattedValue;
        }

        record.FormattedValue = record.Patch == null ? string.Empty : getPatchValue(record.Patch);
        record.ValueComputed = true;
        return record.FormattedValue;
    }

    private static string GetValueSearchText(PatchRecord record)
    {
        // Separate cache for search text. Search should not pretty-print XML because
        // indentation is only a display concern and adds avoidable work.
        if (record.ValueSearchTextComputed)
        {
            return record.ValueSearchText;
        }

        record.ValueSearchText = record.Patch == null ? string.Empty : getPatchValueSearchText(record.Patch);
        record.ValueSearchTextComputed = true;
        return record.ValueSearchText;
    }

    private static bool hasPatchValueField(PatchOperation patch)
    {
        // Cheap existence check used for the expand marker. It intentionally does not
        // stringify or format the value.
        try
        {
            var fi = getFieldCached(patch.GetType(), "value");
            if (fi == null)
            {
                return false;
            }

            return fi.GetValue(patch) != null;
        }
        catch
        {
            return false;
        }
    }

    private static string getPatchValue(PatchOperation patch)
    {
        // Expensive path. Keep it out of indexing/search/drawing collapsed rows. XML
        // containers and nodes are formatted for readability only after expansion.
        try
        {
            var fi = getFieldCached(patch.GetType(), "value");
            if (fi == null)
            {
                return string.Empty;
            }

            var raw = fi.GetValue(patch);
            switch (raw)
            {
                case null:
                    return string.Empty;
                case string s:
                    return maybeFormatXmlString(s);
            }

            var rawType = raw.GetType();
            if (rawType.Name == "XmlContainer")
            {
                if (xmlFormatCache.TryGetValue(raw, out var cached))
                {
                    return cached;
                }

                var nodeField = getFieldCached(rawType, "node") ?? getFieldCached(rawType, "Node");
                if (nodeField?.GetValue(raw) is not XmlNode xn)
                {
                    return string.Empty;
                }

                var formatted = formatXmlNode(xn);
                xmlFormatCache[raw] = formatted;
                return formatted;
            }

            switch (raw)
            {
                case XmlNode xmlNode:
                    return formatXmlNode(xmlNode);
                case IEnumerable<XmlNode> nodeEnum:
                    return string.Join("\n", nodeEnum.Select(formatXmlNode));
            }

            var generic = raw.ToString();
            if (!string.IsNullOrEmpty(generic) && generic != rawType.FullName)
            {
                return maybeFormatXmlString(generic);
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private static string getPatchValueSearchText(PatchOperation patch)
    {
        // Raw value extraction for optional search. Keep this cheaper than
        // getPatchValue: no XmlDocument parsing, no indentation, no display wrapping.
        try
        {
            var fi = getFieldCached(patch.GetType(), "value");
            if (fi == null)
            {
                return string.Empty;
            }

            var raw = fi.GetValue(patch);
            switch (raw)
            {
                case null:
                    return string.Empty;
                case string s:
                    return s;
            }

            var rawType = raw.GetType();
            if (rawType.Name == "XmlContainer")
            {
                var nodeField = getFieldCached(rawType, "node") ?? getFieldCached(rawType, "Node");
                return nodeField?.GetValue(raw) is XmlNode xn ? xn.OuterXml : string.Empty;
            }

            switch (raw)
            {
                case XmlNode xmlNode:
                    return xmlNode.OuterXml;
                case IEnumerable<XmlNode> nodeEnum:
                {
                    var sb = new StringBuilder();
                    foreach (var node in nodeEnum)
                    {
                        if (node == null)
                        {
                            continue;
                        }

                        if (sb.Length > 0)
                        {
                            sb.Append('\n');
                        }

                        sb.Append(node.OuterXml);
                    }

                    return sb.ToString();
                }
            }

            var generic = raw.ToString();
            return !string.IsNullOrEmpty(generic) && generic != rawType.FullName ? generic : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string maybeFormatXmlString(string input)
    {
        // Fast reject non-XML-looking strings. This keeps plain text values from paying
        // XmlDocument parsing costs.
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        if (input.IndexOf('<') < 0 || input.IndexOf('>') < 0)
        {
            return input;
        }

        try
        {
            return formatXmlFragment(input);
        }
        catch
        {
            return input;
        }
    }

    private static string formatXmlNode(XmlNode node)
    {
        if (node == null)
        {
            return string.Empty;
        }

        try
        {
            using var sw = new StringWriter();
            using (var xw = XmlWriter.Create(sw, PrettyXmlSettings))
            {
                if (node is XmlDocument doc)
                {
                    doc.DocumentElement?.WriteTo(xw);
                }
                else
                {
                    node.WriteTo(xw);
                }
            }

            return sw.ToString().Trim();
        }
        catch
        {
            return node.OuterXml;
        }
    }

    private static string formatXmlFragment(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return string.Empty;
        }

        var wrapped = fragment;
        try
        {
            var tempDoc = new XmlDocument();
            try
            {
                tempDoc.LoadXml(fragment);
            }
            catch
            {
                wrapped = $"<root>{fragment}</root>";
                tempDoc.LoadXml(wrapped);
            }

            using var sw = new StringWriter();
            using (var xw = XmlWriter.Create(sw, PrettyXmlSettings))
            {
                if (wrapped == fragment)
                {
                    if (tempDoc.DocumentElement != null)
                    {
                        tempDoc.DocumentElement.WriteTo(xw);
                    }
                }
                else
                {
                    if (tempDoc.DocumentElement == null)
                    {
                        return sw.ToString().Trim();
                    }

                    foreach (XmlNode child in tempDoc.DocumentElement.ChildNodes)
                    {
                        child.WriteTo(xw);
                    }
                }
            }

            return sw.ToString().Trim();
        }
        catch
        {
            return fragment.Trim();
        }
    }
}
