namespace Tagster.Core;

/// <summary>Options controlling an archive scan.</summary>
public sealed record ScanOptions
{
    /// <summary>
    /// How many directory levels below the root to search for sidecars. Kept shallow on purpose:
    /// tagged folders live near the archive root (the author folders), so walking deep photo trees
    /// — or, worst case, a whole drive — is needless and slow.
    /// </summary>
    public int MaxDepth { get; init; } = 4;
}

/// <summary>Outcome of reconciling the index with the sidecars found on disk.</summary>
public sealed record ScanResult(int Added, int Updated, int Removed, int Reidentified, int TotalIndexed);
