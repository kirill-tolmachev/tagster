using Tagster.Data;

namespace Tagster.Tests;

/// <summary>A <see cref="SqliteFolderIndex"/> over a temp-file database, cleaned up on dispose.</summary>
public sealed class TempDb : IDisposable
{
    private readonly string _directory;

    public SqliteFolderIndex Index { get; }

    /// <summary>Full path to the backing database file (for tests that seed it directly).</summary>
    public string DatabasePath { get; }

    public TempDb()
    {
        _directory = Path.Combine(Path.GetTempPath(), "tagster-tests-db", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "index.db");
        Index = new SqliteFolderIndex(DatabasePath);
    }

    public void Dispose()
    {
        Index.Dispose();
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* best effort */ }
    }
}
