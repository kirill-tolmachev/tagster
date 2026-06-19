namespace Tagster.Core;

/// <summary>
/// Normalizes tags for storage and comparison. Uses invariant-culture lowercasing, which is
/// Unicode-correct for Cyrillic and Latin alike — the basis for case-insensitive Cyrillic search.
/// </summary>
public static class TagNormalizer
{
    /// <summary>Trim, collapse internal whitespace, and lowercase (invariant culture).</summary>
    public static string Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
        var collapsed = string.Join(' ', tag.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.ToLowerInvariant();
    }

    /// <summary>
    /// Produce a stable, de-duplicated list of display tag names: blanks dropped, duplicates
    /// (by normalized form) removed keeping the first spelling seen, ordered deterministically.
    /// </summary>
    public static IReadOnlyList<string> NormalizeDisplayList(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<(string Norm, string Display)>();

        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var display = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var norm = display.ToLowerInvariant();
            if (norm.Length == 0) continue;
            if (seen.Add(norm)) items.Add((norm, display));
        }

        items.Sort(static (a, b) => string.CompareOrdinal(a.Norm, b.Norm));
        return items.Select(static i => i.Display).ToList();
    }
}
