using Tagster.Core;

namespace Tagster.Tests;

public class SqliteFolderIndexTests
{
    private static TaggedFolder Folder(string name, params string[] tags) => new()
    {
        Id = Guid.NewGuid(),
        RootPath = @"C:\archive",
        RelativePath = name,
        Name = name,
        Tags = tags,
        UpdatedUtc = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task Upsert_and_get_roundtrips()
    {
        using var db = new TempDb();
        var f = Folder("Иванов", "док", "война");
        await db.Index.UpsertAsync(f);

        var got = await db.Index.GetByIdAsync(f.Id);

        Assert.NotNull(got);
        Assert.Equal("Иванов", got!.Name);
        Assert.Equal(new[] { "война", "док" }, got.Tags.OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Upsert_replaces_tags()
    {
        using var db = new TempDb();
        var f = Folder("a", "x", "y");
        await db.Index.UpsertAsync(f);
        await db.Index.UpsertAsync(f with { Tags = ["z"] });

        var got = await db.Index.GetByIdAsync(f.Id);
        Assert.Equal(new[] { "z" }, got!.Tags);
    }

    [Fact]
    public async Task Search_include_all_and_exclude()
    {
        using var db = new TempDb();
        await db.Index.UpsertAsync(Folder("Иванов", "док", "война"));
        await db.Index.UpsertAsync(Folder("Петров", "док", "портрет"));
        await db.Index.UpsertAsync(Folder("Сидоров", "портрет"));

        var result = await db.Index.SearchAsync(new SearchQuery { Include = ["док"], Exclude = ["война"] });

        Assert.Equal(new[] { "Петров" }, result.Select(f => f.Name));
    }

    [Fact]
    public async Task Search_include_any()
    {
        using var db = new TempDb();
        await db.Index.UpsertAsync(Folder("Иванов", "док", "война"));
        await db.Index.UpsertAsync(Folder("Сидоров", "портрет"));

        var result = await db.Index.SearchAsync(
            new SearchQuery { Include = ["война", "портрет"], IncludeMatch = TagMatch.Any });

        Assert.Equal(new[] { "Иванов", "Сидоров" },
            result.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Search_by_name_is_cyrillic_case_insensitive()
    {
        using var db = new TempDb();
        await db.Index.UpsertAsync(Folder("Иванов", "док"));
        await db.Index.UpsertAsync(Folder("Петров", "док"));

        var result = await db.Index.SearchAsync(new SearchQuery { NameContains = "ИВАН" });

        Assert.Equal(new[] { "Иванов" }, result.Select(f => f.Name));
    }

    [Fact]
    public async Task Tag_counts_aggregate_across_folders()
    {
        using var db = new TempDb();
        await db.Index.UpsertAsync(Folder("a", "док", "война"));
        await db.Index.UpsertAsync(Folder("b", "док"));

        var counts = await db.Index.GetTagCountsAsync();

        Assert.Equal(2, counts.Single(c => c.Name == "док").Count);
        Assert.Equal(1, counts.Single(c => c.Name == "война").Count);
    }

    [Fact]
    public async Task Remove_deletes_folder_and_its_tags()
    {
        using var db = new TempDb();
        var f = Folder("a", "x");
        await db.Index.UpsertAsync(f);
        await db.Index.RemoveAsync(f.Id);

        Assert.Null(await db.Index.GetByIdAsync(f.Id));
        Assert.Empty(await db.Index.GetTagCountsAsync());
    }

    [Fact]
    public async Task GetAll_filters_by_root()
    {
        using var db = new TempDb();
        await db.Index.UpsertAsync(Folder("a", "x"));
        await db.Index.UpsertAsync(new TaggedFolder
        {
            Id = Guid.NewGuid(),
            RootPath = @"D:\other",
            RelativePath = "b",
            Name = "b",
            Tags = ["y"],
            UpdatedUtc = DateTimeOffset.UnixEpoch,
        });

        var all = await db.Index.GetAllAsync(@"C:\archive");

        Assert.Single(all);
        Assert.Equal("a", all[0].Name);
    }

    [Fact]
    public async Task Search_and_counts_can_be_scoped_to_a_root()
    {
        using var db = new TempDb();
        await db.Index.UpsertAsync(Folder("a", "док"));
        await db.Index.UpsertAsync(new TaggedFolder
        {
            Id = Guid.NewGuid(),
            RootPath = @"D:\other",
            RelativePath = "b",
            Name = "b",
            Tags = ["док"],
            UpdatedUtc = DateTimeOffset.UnixEpoch,
        });

        var scoped = await db.Index.SearchAsync(new SearchQuery { Include = ["док"] }, @"C:\archive");
        Assert.Single(scoped);
        Assert.Equal("a", scoped[0].Name);

        Assert.Equal(1, (await db.Index.GetTagCountsAsync(@"C:\archive")).Single(c => c.Name == "док").Count);
        Assert.Equal(2, (await db.Index.SearchAsync(new SearchQuery { Include = ["док"] })).Count);
    }
}
