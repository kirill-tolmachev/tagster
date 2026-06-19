using Tagster.Core;

namespace Tagster.Tests;

public class TagManagerTests
{
    private static (TaggingService Tagging, TagManager Manager, SidecarStore Sidecars, TempDb Db) Make()
    {
        var sidecars = new SidecarStore();
        var db = new TempDb();
        var time = new StubTimeProvider(DateTimeOffset.UnixEpoch);
        return (new TaggingService(sidecars, db.Index, time), new TagManager(sidecars, db.Index, time), sidecars, db);
    }

    [Fact]
    public async Task Rename_updates_sidecars_and_index()
    {
        using var temp = new TempDir();
        var (tagging, manager, sidecars, db) = Make();
        using var _ = db;
        var a = temp.CreateFolder("A");
        var b = temp.CreateFolder("B");
        await tagging.SetTagsAsync(temp.Path, a, ["док", "война"]);
        await tagging.SetTagsAsync(temp.Path, b, ["док"]);

        var affected = await manager.RenameAsync(temp.Path, "док", "документ");

        Assert.Equal(2, affected);
        Assert.Contains("документ", sidecars.TryRead(a)!.Tags);
        Assert.DoesNotContain("док", sidecars.TryRead(a)!.Tags);
        Assert.Equal(2, (await db.Index.SearchAsync(new SearchQuery { Include = ["документ"] }, temp.Path)).Count);
        Assert.Empty(await db.Index.SearchAsync(new SearchQuery { Include = ["док"] }, temp.Path));
    }

    [Fact]
    public async Task Rename_into_existing_tag_merges_and_dedupes()
    {
        using var temp = new TempDir();
        var (tagging, manager, sidecars, db) = Make();
        using var _ = db;
        var a = temp.CreateFolder("A");
        await tagging.SetTagsAsync(temp.Path, a, ["док", "война"]);

        var affected = await manager.RenameAsync(temp.Path, "война", "док");

        Assert.Equal(1, affected);
        Assert.Equal(new[] { "док" }, sidecars.TryRead(a)!.Tags);
    }

    [Fact]
    public async Task Delete_removes_tag_and_deindexes_emptied_folders()
    {
        using var temp = new TempDir();
        var (tagging, manager, sidecars, db) = Make();
        using var _ = db;
        var a = temp.CreateFolder("A");
        var b = temp.CreateFolder("B");
        await tagging.SetTagsAsync(temp.Path, a, ["док", "война"]);
        await tagging.SetTagsAsync(temp.Path, b, ["док"]);

        var affected = await manager.DeleteAsync(temp.Path, "док");

        Assert.Equal(2, affected);
        Assert.Equal(new[] { "война" }, sidecars.TryRead(a)!.Tags);
        Assert.Null(sidecars.TryRead(b));
        Assert.Empty(await db.Index.SearchAsync(new SearchQuery { Include = ["док"] }, temp.Path));
        Assert.Single(await db.Index.SearchAsync(new SearchQuery { Include = ["война"] }, temp.Path));
    }
}
