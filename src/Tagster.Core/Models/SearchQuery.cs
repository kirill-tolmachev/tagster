namespace Tagster.Core;

/// <summary>How the included tags combine.</summary>
public enum TagMatch
{
    /// <summary>A folder must carry every included tag.</summary>
    All,

    /// <summary>A folder must carry at least one included tag.</summary>
    Any,
}

/// <summary>A folder search request: include/exclude tags plus an optional name filter.</summary>
public sealed record SearchQuery
{
    /// <summary>Tags the folder must have (combined per <see cref="IncludeMatch"/>).</summary>
    public IReadOnlyList<string> Include { get; init; } = [];

    /// <summary>Tags the folder must not have (any match excludes the folder).</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>Whether included tags combine as AND (<see cref="TagMatch.All"/>) or OR.</summary>
    public TagMatch IncludeMatch { get; init; } = TagMatch.All;

    /// <summary>Case-insensitive substring the folder name must contain (optional).</summary>
    public string? NameContains { get; init; }
}
