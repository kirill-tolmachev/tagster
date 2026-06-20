using System.IO;

namespace Tagster.App;

/// <summary>Well-known on-disk locations for the app's local data.</summary>
internal static class AppPaths
{
    /// <summary><c>%AppData%\Tagster</c> — holds the rebuildable SQLite index.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tagster");

    public static string IndexDatabasePath { get; } = Path.Combine(DataDirectory, "index.db");

    /// <summary><c>%AppData%\Tagster\logs</c> — rolling Serilog text logs.</summary>
    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");
}
