using System.Text.Json.Serialization;

namespace Tagster.App;

/// <summary>How the global tag panel is ordered (per-folder tags are always alphabetical).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TagSortMode
{
    /// <summary>Alphabetical, culture-aware — the same order as the folder list.</summary>
    Name,

    /// <summary>Most-used first, by archive-wide folder count.</summary>
    Count,
}

/// <summary>User preferences persisted to <c>%AppData%\Tagster\settings.json</c>.</summary>
public sealed class AppSettings
{
    public bool ReopenLastArchive { get; set; } = true;
    public string? LastArchivePath { get; set; }
    public TagSortMode TagSort { get; set; } = TagSortMode.Name;
}
