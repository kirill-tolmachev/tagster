using Microsoft.Data.Sqlite;
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

    [Fact]
    public async Task Search_and_get_all_exceed_sqlite_parameter_limit()
    {
        using var db = new TempDb();

        // More folders than SQLITE_MAX_VARIABLE_NUMBER (32766): tag hydration must not bind one host
        // parameter per matched id, or both SearchAsync and GetAllAsync (used by Rescan) would throw
        // "too many SQL variables" on a large archive.
        const int count = 35_000;
        BulkSeed(db.DatabasePath, count, sharedTag: "bulk");

        Assert.Equal(count, (await db.Index.GetAllAsync(@"C:\archive")).Count);
        Assert.Equal(count, (await db.Index.SearchAsync(new SearchQuery())).Count);

        var byTag = await db.Index.SearchAsync(new SearchQuery { Include = ["bulk"] });
        Assert.Equal(count, byTag.Count);
        Assert.All(byTag, f => Assert.Equal(new[] { "bulk" }, f.Tags));
    }

    /// <summary>
    /// Insert <paramref name="count"/> folders (each carrying <paramref name="sharedTag"/>) straight
    /// into the database file in a single transaction — far faster than per-folder UpsertAsync when we
    /// need tens of thousands of rows.
    /// </summary>
    private static void BulkSeed(string databasePath, int count, string sharedTag)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var folderCmd = connection.CreateCommand();
        folderCmd.Transaction = transaction;
        folderCmd.CommandText =
            "INSERT INTO folders (id, root_path, relative_path, name, name_norm, updated_utc) " +
            "VALUES ($id, 'C:\\archive', $name, $name, $name, '1970-01-01T00:00:00.0000000+00:00');";
        var id = folderCmd.CreateParameter(); id.ParameterName = "$id"; folderCmd.Parameters.Add(id);
        var name = folderCmd.CreateParameter(); name.ParameterName = "$name"; folderCmd.Parameters.Add(name);

        using var tagCmd = connection.CreateCommand();
        tagCmd.Transaction = transaction;
        tagCmd.CommandText = "INSERT INTO folder_tags (folder_id, tag, tag_norm) VALUES ($fid, $tag, $tag);";
        var fid = tagCmd.CreateParameter(); fid.ParameterName = "$fid"; tagCmd.Parameters.Add(fid);
        var tag = tagCmd.CreateParameter(); tag.ParameterName = "$tag"; tag.Value = sharedTag; tagCmd.Parameters.Add(tag);

        for (var i = 0; i < count; i++)
        {
            var guid = Guid.NewGuid().ToString();
            id.Value = guid;
            name.Value = "f" + i;
            folderCmd.ExecuteNonQuery();

            fid.Value = guid;
            tagCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
