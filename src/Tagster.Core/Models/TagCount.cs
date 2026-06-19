namespace Tagster.Core;

/// <summary>A tag and the number of folders carrying it (drives the tag filter panel).</summary>
public sealed record TagCount(string Name, int Count);
