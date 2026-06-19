using Tagster.Core;

namespace Tagster.Tests;

public class SidecarStoreTests
{
    [Fact]
    public void Write_then_read_roundtrips_cyrillic_tags_and_cover()
    {
        using var temp = new TempDir();
        var folder = temp.CreateFolder("Иванов");
        var store = new SidecarStore();
        var id = Guid.NewGuid();

        store.Write(folder, new Sidecar
        {
            Id = id,
            Tags = ["док", "портрет"],
            Cover = new CoverInfo { Source = ".tagster_cover.jpg", SetUtc = DateTimeOffset.UnixEpoch },
            UpdatedUtc = DateTimeOffset.UnixEpoch,
        });

        var read = store.TryRead(folder);

        Assert.NotNull(read);
        Assert.Equal(id, read!.Id);
        Assert.Equal(new[] { "док", "портрет" }, read.Tags);
        Assert.Equal(".tagster_cover.jpg", read.Cover!.Source);
    }

    [Fact]
    public void Written_file_is_hidden_and_stores_cyrillic_literally()
    {
        using var temp = new TempDir();
        var folder = temp.CreateFolder("author");
        new SidecarStore().Write(folder, new Sidecar { Id = Guid.NewGuid(), Tags = ["война"] });

        var path = Path.Combine(folder, SidecarStore.FileName);
        Assert.True(File.Exists(path));
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
        Assert.Contains("война", File.ReadAllText(path)); // literal, not \uXXXX escaped
    }

    [Fact]
    public void Write_overwrites_existing_hidden_sidecar()
    {
        using var temp = new TempDir();
        var folder = temp.CreateFolder("author");
        var store = new SidecarStore();
        var id = Guid.NewGuid();

        store.Write(folder, new Sidecar { Id = id, Tags = ["a"] });
        store.Write(folder, new Sidecar { Id = id, Tags = ["a", "b"] });

        Assert.Equal(new[] { "a", "b" }, store.TryRead(folder)!.Tags);
    }

    [Fact]
    public void TryRead_missing_returns_null()
    {
        using var temp = new TempDir();
        Assert.Null(new SidecarStore().TryRead(temp.CreateFolder("empty")));
    }

    [Fact]
    public void Delete_removes_sidecar()
    {
        using var temp = new TempDir();
        var folder = temp.CreateFolder("author");
        var store = new SidecarStore();

        store.Write(folder, new Sidecar { Id = Guid.NewGuid(), Tags = ["x"] });
        store.Delete(folder);

        Assert.Null(store.TryRead(folder));
    }
}
