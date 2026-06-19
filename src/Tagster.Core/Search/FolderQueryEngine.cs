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
