namespace Tagster.Core;

/// <summary>
/// Pure, in-memory implementation of the search semantics (include AND/ANY, exclude NOT, name
/// contains). This is the canonical definition of "what matches" — used directly for fast
/// in-memory filtering and mirrored by the SQLite index.
/// </summary>
public static class FolderQueryEngine
{
    public static IEnumerable<TaggedFolder> Filter(IEnumerable<TaggedFolder> folders, SearchQuery query)
    {
        var include = NormalizeSet(query.Include);
        var exclude = NormalizeSet(query.Exclude);
        var name = string.IsNullOrWhiteSpace(query.NameContains)
            ? null
            : query.NameContains.Trim().ToLowerInvariant();

        foreach (var folder in folders)
        {
            var tags = NormalizeSet(folder.Tags);

            if (include.Count > 0)
            {
                var matches = query.IncludeMatch == TagMatch.All
                    ? include.IsSubsetOf(tags)
                    : include.Overlaps(tags);
                if (!matches) continue;
            }

            if (exclude.Count > 0 && exclude.Overlaps(tags)) continue;

            if (name is not null &&
                !folder.Name.ToLowerInvariant().Contains(name, StringComparison.Ordinal))
                continue;

            yield return folder;
        }
    }

    /// <summary>
    /// Tally the tags carried by an already-matched set of folders: each normalized tag mapped to
    /// how many of those folders carry it. This powers the tag panel's co-occurrence narrowing and
    /// live counts — the keys are exactly the tags that can still combine with the current filter,
    /// and the values are the per-tag folder counts shown beside them. A tag repeated within one
    /// folder is counted once (folders, not occurrences).
    /// </summary>
    public static IReadOnlyDictionary<string, int> CoOccurringTagCounts(IEnumerable<TaggedFolder> folders)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var folder in folders)
            foreach (var norm in folder.Tags
                         .Select(TagNormalizer.Normalize)
                         .Where(n => n.Length > 0)
                         .Distinct(StringComparer.Ordinal))
                counts[norm] = counts.GetValueOrDefault(norm) + 1;
        return counts;
    }

    private static HashSet<string> NormalizeSet(IEnumerable<string> tags)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            var norm = TagNormalizer.Normalize(tag);
            if (norm.Length > 0) set.Add(norm);
        }
        return set;
    }
}
