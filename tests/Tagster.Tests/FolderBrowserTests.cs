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

        var result = browser.ListFolders(temp.Path);

        Assert.Equal(new[] { "Petrov", "Иванов", "Сидоров" },
            result.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.True(result.Single(f => f.Name == "Иванов").IsTagged);
        Assert.Equal(new[] { "док" }, result.Single(f => f.Name == "Иванов").Tags);
        Assert.False(result.Single(f => f.Name == "Petrov").IsTagged);
    }

    [Fact]
    public void Missing_directory_returns_empty()
    {
        var browser = new FolderBrowser(new SidecarStore());
        Assert.Empty(browser.ListFolders(@"Z:\does\not\exist\tagster"));
    }
}
