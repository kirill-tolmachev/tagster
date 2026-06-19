using Tagster.Data;

namespace Tagster.Tests;

/// <summary>A <see cref="SqliteFolderIndex"/> over a temp-file database, cleaned up on dispose.</summary>
public sealed class TempDb : IDisposable
{
    private readonly string _directory;

    public SqliteFolderIndex Index { get; }

    public TempDb()
    {
        _directory = Path.Combine(Path.GetTempPath(), "tagster-tests-db", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Index = new SqliteFolderIndex(Path.Combine(_directory, "index.db"));
    }

    public void Dispose()
    {
        Index.Dispose();
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* best effort */ }
    }
}
