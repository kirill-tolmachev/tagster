namespace Tagster.Core;

/// <summary>
/// A folder that carries tags, as represented in the search index. Identity is the stable
/// <see cref="Id"/>; <see cref="RelativePath"/> is stored relative to <see cref="RootPath"/>
/// (with '/'), which keeps the index portable across machines and drive letters.
/// </summary>
public sealed record TaggedFolder
{
    public required Guid Id { get; init; }
    public required string RootPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>The folder's absolute path, recomposed from the root and relative path.</summary>
    public string AbsolutePath => PathUtil.ToAbsolute(RootPath, RelativePath);
}
