namespace Tagster.App;

/// <summary>User preferences persisted to <c>%AppData%\Tagster\settings.json</c>.</summary>
public sealed class AppSettings
{
    public bool ReopenLastArchive { get; set; } = true;
    public string? LastArchivePath { get; set; }
}
