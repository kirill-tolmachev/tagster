using System.Text;
using Tagster.Core;

namespace Tagster.Tests;

public class AtomicFileTests
{
    [Fact]
    public void Write_creates_file_with_contents()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "data.bin");

        AtomicFile.Write(path, new byte[] { 1, 2, 3 });

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Write_replaces_existing_contents()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "data.txt");
        AtomicFile.Write(path, "old", Encoding.UTF8);

        AtomicFile.Write(path, "new", Encoding.UTF8);

        Assert.Equal("new", File.ReadAllText(path));
    }

    [Fact]
    public void Write_leaves_no_temp_files_behind()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "data.txt");

        AtomicFile.Write(path, "hello", Encoding.UTF8);

        // The orphaned-.tmp bug: a successful write must leave exactly the target, no temp clutter.
        var only = Assert.Single(Directory.GetFiles(dir.Path));
        Assert.Equal("data.txt", Path.GetFileName(only));
    }

    [Fact]
    public void Write_replaces_a_hidden_file_and_keeps_its_attributes()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, ".sidecar");

        AtomicFile.Write(path, "v1", Encoding.UTF8, FileAttributes.Hidden);
        // Overwriting a hidden destination must succeed — the Windows MoveFileEx quirk this guards against.
        AtomicFile.Write(path, "v2", Encoding.UTF8, FileAttributes.Hidden);

        Assert.Equal("v2", File.ReadAllText(path));
        if (OperatingSystem.IsWindows())
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
    }
}
