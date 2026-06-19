namespace Tagster.Tests;

/// <summary>A throwaway directory tree under the system temp folder, deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tagster-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path);
    }

    public string CreateFolder(string relativeName)
    {
        var full = System.IO.Path.Combine(Path, relativeName);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(Path)) return;

            // Clear hidden/system attributes so the sidecar files can be deleted.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* best effort */ }
            }
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
