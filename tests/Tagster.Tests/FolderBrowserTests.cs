using Tagster.Core;

namespace Tagster.Tests;

public class FolderBrowserTests
{
    [Fact]
    public void Lists_visible_subfolders_and_marks_tagged()
    {
        using var temp = new TempDir();
        var sidecars = new SidecarStore();
        var browser = new FolderBrowser(sidecars);

        temp.CreateFolder("Сидоров");
        var ivanov = temp.CreateFolder("Иванов");
        temp.CreateFolder("Petrov");
        sidecars.Write(ivanov, new Sidecar { Id = Guid.NewGuid(), Tags = ["док"] });

        // A protected OS-style folder should be skipped.
        var hidden = temp.CreateFolder("$protected");
        new DirectoryInfo(hidden).Attributes |= FileAttributes.Hidden | FileAttributes.System;

        var result = browser.ListEntries(temp.Path);

        Assert.Equal(new[] { "Petrov", "Иванов", "Сидоров" },
            result.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.True(result.Single(f => f.Name == "Иванов").IsTagged);
        Assert.Equal(new[] { "док" }, result.Single(f => f.Name == "Иванов").Tags);
        Assert.False(result.Single(f => f.Name == "Petrov").IsTagged);
    }

    [Fact]
    public void Lists_latin_folders_above_cyrillic()
    {
        using var temp = new TempDir();
        var browser = new FolderBrowser(new SidecarStore());

        temp.CreateFolder("Сидоров");
        temp.CreateFolder("Иванов");
        temp.CreateFolder("Smith");
        temp.CreateFolder("Adams");

        var result = browser.ListEntries(temp.Path);

        Assert.Equal(new[] { "Adams", "Smith", "Иванов", "Сидоров" },
            result.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void Lists_files_after_folders_and_hides_hidden_or_system()
    {
        using var temp = new TempDir();
        var browser = new FolderBrowser(new SidecarStore());

        temp.CreateFolder("Album");
        File.WriteAllText(Path.Combine(temp.Path, "photo.jpg"), "x");
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "x");

        // Tagster's own artifacts must stay out of the listing: the sidecar is hidden-only,
        // the cover files are hidden+system. Both kinds should be skipped.
        WriteWithAttributes(temp, ".tagster", FileAttributes.Hidden);
        WriteWithAttributes(temp, "desktop.ini", FileAttributes.Hidden | FileAttributes.System);

        var result = browser.ListEntries(temp.Path);

        // Folder first, then visible files in display order; artifacts omitted.
        Assert.Equal(new[] { "Album", "notes.txt", "photo.jpg" }, result.Select(e => e.Name).ToArray());
        Assert.False(result.Single(e => e.Name == "Album").IsFile);
        Assert.True(result.Single(e => e.Name == "photo.jpg").IsFile);
        Assert.True(result.Single(e => e.Name == "notes.txt").IsFile);
    }

    [Fact]
    public void Missing_directory_returns_empty()
    {
        var browser = new FolderBrowser(new SidecarStore());
        Assert.Empty(browser.ListEntries(@"Z:\does\not\exist\tagster"));
    }

    [Fact]
    public void Skips_reserved_device_name_entry_instead_of_aborting_the_listing()
    {
        using var temp = new TempDir();
        var browser = new FolderBrowser(new SidecarStore());

        temp.CreateFolder("Album");
        File.WriteAllText(Path.Combine(temp.Path, "photo.jpg"), "x");

        // A reserved DOS device name ("nul") survives EnumerateFiles but throws IOException the moment
        // its .Attributes are read — which used to bubble up and abort the whole listing, so the folder
        // "showed nothing". Only the \\?\ extended-length path can create such an entry.
        var nul = @"\\?\" + Path.Combine(temp.Path, "nul");
        using (File.Create(nul)) { }
        try
        {
            var result = browser.ListEntries(temp.Path);

            // The bad entry is skipped; the real folder and file still list.
            Assert.Equal(new[] { "Album", "photo.jpg" }, result.Select(e => e.Name).ToArray());
        }
        finally
        {
            // Delete via \\?\ before TempDir cleanup, which can't remove it through a normal path.
            File.Delete(nul);
        }
    }

    private static void WriteWithAttributes(TempDir temp, string name, FileAttributes attributes)
    {
        var path = Path.Combine(temp.Path, name);
        File.WriteAllText(path, "x");
        File.SetAttributes(path, attributes);
    }
}
