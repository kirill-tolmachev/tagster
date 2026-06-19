using System.Text.Json.Serialization;

namespace Tagster.Core;

/// <summary>
/// The per-folder source of truth, persisted as a hidden <c>.tagster</c> JSON file inside the
/// tagged folder. It travels with the folder, so tags survive moves, copies, and restores.
/// </summary>
public sealed record Sidecar
{
    /// <summary>Schema version of the sidecar file.</summary>
    [JsonPropertyName("v")]
    public int Version { get; init; } = 1;

    /// <summary>Stable identity used to re-link the folder after a rename or move.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Display tag names assigned to the folder.</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Optional folder cover/preview metadata.</summary>
    [JsonPropertyName("cover")]
    public CoverInfo? Cover { get; init; }

    /// <summary>Timestamp of the last change (UTC).</summary>
    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; init; }
}

/// <summary>Metadata describing a folder's cover image (set from within Tagster).</summary>
public sealed record CoverInfo
{
    /// <summary>Folder-relative file name of the source cover image (e.g. <c>.tagster_cover.jpg</c>).</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>When the cover was set (UTC).</summary>
    [JsonPropertyName("setUtc")]
    public DateTimeOffset SetUtc { get; init; }
}
