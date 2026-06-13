using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace VisualXMLPatches;

internal partial class VisualXMLPatchesMod
{
    // Reflection helpers for Verse PatchOperation internals. FieldInfo objects are cached, while per-patch values are extracted only at controlled rebuild/lazy points.

    private static FieldInfo getFieldCached(Type t, string fieldName)
    {
        // Field lookup is cached separately from field value extraction. Values are
        // per patch and can change/contain different objects; FieldInfo is per type.
        var key = (t, fieldName);
        if (fieldCache.TryGetValue(key, out var fi))
        {
            return fi;
        }

        fi = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        fieldCache[key] = fi;
        return fi;
    }

    private static string getPatchXPath(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "xpath");
            if (fi?.GetValue(patch) is string s && !string.IsNullOrEmpty(s))
            {
                return s;
            }
        }
        catch
        {
            // ignored
        }

        return "(no xpath)";
    }

    private static bool getNeverSucceeded(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "neverSucceeded");
            if (fi?.GetValue(patch) is bool b)
            {
                return b;
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }

    private static string getPatchAttribute(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "attribute");
            if (fi?.GetValue(patch) is string s && !string.IsNullOrEmpty(s))
            {
                return s;
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private static string getPatchSourceFile(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "sourceFile");
            if (fi?.GetValue(patch) is string s && !string.IsNullOrEmpty(s))
            {
                return s.Replace('/', Path.DirectorySeparatorChar);
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private static string getPatchMods(PatchOperation patch)
    {
        try
        {
            var fi = getFieldCached(patch.GetType(), "mods");
            if (fi == null)
            {
                return string.Empty;
            }

            var raw = fi.GetValue(patch);
            switch (raw)
            {
                case null:
                    return string.Empty;
                case IEnumerable<object> list:
                {
                    var items = list.Select(o => o?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    return items.Count == 0 ? string.Empty : $"mods: {string.Join(", ", items)}";
                }
                default:
                {
                    var str = raw.ToString();
                    return string.IsNullOrEmpty(str) ? string.Empty : $"mods: {str}";
                }
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private static IEnumerable<PatchOperation> getSubOperations(PatchOperation patch)
    {
        var fi = getFieldCached(patch.GetType(), "operations");
        if (fi == null)
        {
            yield break;
        }

        var raw = fi.GetValue(patch);
        if (raw is not IEnumerable<PatchOperation> enumOps)
        {
            yield break;
        }

        foreach (var op in enumOps)
        {
            if (op != null)
            {
                yield return op;
            }
        }
    }

    // The previous implementation also built a verbose operations detail block for
    // height calculation, but it was never drawn. That unused work was removed
    // instead of cached because caching dead UI data would only preserve the cost in
    // a different place. getPatchOperationsSummary remains because it is cheap, shown
    // in collapsed rows when xpath is absent, and included in search.

    private static string getPatchOperationsSummary(PatchOperation patch)
    {
        var ops = getSubOperations(patch).Take(10).ToList();
        return ops.Count == 0
            ? string.Empty
            : $"operations: {string.Join(", ", ops.Select(o => o.GetType().Name.Replace("PatchOperation", string.Empty)))}";
    }
}
