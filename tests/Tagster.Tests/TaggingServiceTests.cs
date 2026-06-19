using Tagster.Core;

namespace Tagster.Tests;

public class TaggingServiceTests
{
    private static (TaggingService Service, SidecarStore Sidecars, TempDb Db) Make()
    {
        var sidecars = new SidecarStore();
        var db = new TempDb();
        var service = new TaggingService(sidecars, db.Index, new StubTimeProvider(DateTimeOffset.UnixEpoch));
        return (service, sidecars, db);
    }

    [Fact]
    public async Task SetTags_writes_sidecar_and_indexes_folder()
    {
        using var temp = new TempDir();
        var (service, sidecars, db) = Make();
        using var _ = db;
        var folder = temp.CreateFolder("Иванов");

        await service.SetTagsAsync(temp.Path, folder, ["Док", "Война"]);

        var sidecar = sidecars.TryRead(folder);
        Assert.NotNull(sidecar);
        Assert.Equal(new[] { "Война", "Док" }, sidecar!.Tags); // normalized display list, ordered

        var indexed = await db.Index.SearchAsync(new SearchQuery { Include = ["док"] });
        Assert.Single(indexed);
        Assert.Equal("Иванов", indexed[0].Name);
    }

    [Fact]
    public async Task AddTags_merges_with_existing()
    {
        using var temp = new TempDir();
        var (service, sidecars, db) = Make();
        using var _ = db;
        var folder = temp.CreateFolder("a");

        await service.SetTagsAsync(temp.Path, folder, ["x"]);
        await service.AddTagsAsync(temp.Path, folder, ["y"]);

        Assert.Equal(new[] { "x", "y" }, sidecars.TryRead(folder)!.Tags);
    }

    [Fact]
    public async Task RemoveTags_removes_specified_case_insensitively()
    {
        using var temp = new TempDir();
        var (service, sidecars, db) = Make();
        using var _ = db;
        var folder = temp.CreateFolder("a");

        await service.SetTagsAsync(temp.Path, folder, ["x", "y"]);
        await service.RemoveTagsAsync(temp.Path, folder, ["X"]);

        Assert.Equal(new[] { "y" }, sidecars.TryRead(folder)!.Tags);
    }

    [Fact]
    public async Task Clearing_all_tags_deletes_sidecar_and_deindexes()
    {
        using var temp = new TempDir();
        var (service, sidecars, db) = Make();
        using var _ = db;
        var folder = temp.CreateFolder("a");

        await service.SetTagsAsync(temp.Path, folder, ["x"]);
        await service.SetTagsAsync(temp.Path, folder, []);

        Assert.Null(sidecars.TryRead(folder));
        Assert.Empty(await db.Index.GetAllAsync(temp.Path));
    }

    [Fact]
    public async Task Identity_is_stable_across_edits()
    {
        using var temp = new TempDir();
        var (service, sidecars, db) = Make();
        using var _ = db;
        var folder = temp.CreateFolder("a");

        await service.SetTagsAsync(temp.Path, folder, ["x"]);
        var first = sidecars.TryRead(folder)!.Id;
        await service.AddTagsAsync(temp.Path, folder, ["y"]);
        var second = sidecars.TryRead(folder)!.Id;

        Assert.Equal(first, second);
    }
}
