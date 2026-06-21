namespace Tagster.Core;

/// <summary>
/// Pure helpers behind add-tag autocomplete. Surfacing existing tags (and reusing their exact
/// spelling) is what stops a mistyped tag from silently becoming a new near-duplicate — the index
/// groups by <see cref="TagNormalizer"/>, so matching here uses the same normalized form.
/// </summary>
public static class TagSuggester
{
    /// <summary>
    /// The archive tags not already on the folder, preserving input order (callers pass them
    /// best-first). This is the pool the autocomplete box filters as the user types.
    /// </summary>
    public static IReadOnlyList<string> Available(IEnumerable<string> allTags, IEnumerable<string> applied)
    {
        var appliedNorm = applied
            .Select(TagNormalizer.Normalize)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        return allTags
            .Where(t => !appliedNorm.Contains(TagNormalizer.Normalize(t)))
            .ToList();
    }

    /// <summary>
    /// The existing tag whose normalized form equals <paramref name="query"/>, or null if none.
    /// A non-null result means the text is already a known tag and can be added as-is — returned in
    /// its canonical spelling, so no duplicate variant is created; null means it would be new.
    /// </summary>
    public static string? ExactMatch(IEnumerable<string> allTags, string query)
    {
        var q = TagNormalizer.Normalize(query);
        if (q.Length == 0) return null;
        return allTags.FirstOrDefault(t => TagNormalizer.Normalize(t) == q);
    }
}
