namespace Tagster.Core;

/// <summary>Options controlling an archive scan.</summary>
public sealed record ScanOptions
{
    /// <summary>How many directory levels below the root to search for sidecars.</summary>
    public int MaxDepth { get; init; } = 16;
}

/// <summary>Outcome of reconciling the index with the sidecars found on disk.</summary>
public sealed record ScanResult(int Added, int Updated, int Removed, int Reidentified, int TotalIndexed);
