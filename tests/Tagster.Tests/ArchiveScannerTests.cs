using Tagster.Core;

namespace Tagster.Tests;

public class ArchiveScannerTests
{
    private static (ArchiveScanner Scanner, SidecarStore Sidecars, TempDb Db) Make()
    {
        var sidecars = new SidecarStore();
        var db = new TempDb();
        var scanner = new ArchiveScanner(sidecars, db.Index, new StubTimeProvider(DateTimeOffset.UnixEpoch));
        return (scanner, sidecars, db);
    }

    private static void WriteTags(SidecarStore sidecars, string folder, Guid id, params string[] tags)
        => sidecars.Write(folder, new Sidecar { Id = id, Tags = tags, UpdatedUtc = DateTimeOffset.UnixEpoch });

    [Fact]
    public async Task Scan_indexes_only_folders_with_tagged_sidecars()
    {
        using var temp = new TempDir();
        var (scanner, sidecars, db) = Make();
        using var _ = db;

        WriteTags(sidecars, temp.CreateFolder("Иванов"), Guid.NewGuid(), "док");
        WriteTags(sidecars, temp.CreateFolder("Петров"), Guid.NewGuid(), "портрет");
        temp.CreateFolder("Untagged");

        var result = await scanner.RescanAsync(temp.Path);

        Assert.Equal(2, result.Added);
        Assert.Equal(2, result.TotalIndexed);
        Assert.Equal(2, (await db.Index.GetAllAsync(temp.Path)).Count);
    }

    [Fact]
    public async Task Rescan_tracks_a_renamed_folder_by_identity()
    {
        using var temp = new TempDir();
        var (scanner, sidecars, db) = Make();
        using var _ = db;

        var id = Guid.NewGuid();
        var original = temp.CreateFolder("Before");
        WriteTags(sidecars, original, id, "док");
        await scanner.RescanAsync(temp.Path);

        // Rename on disk; the sidecar (carrying its id) moves with the folder.
        Directory.Move(original, Path.Combine(temp.Path, "After"));

        var result = await scanner.RescanAsync(temp.Path);

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.Equal(1, result.Updated);

        var folder = await db.Index.GetByIdAsync(id);
        Assert.Equal("After", folder!.Name);
        Assert.Equal("After", folder.RelativePath);
    }

    [Fact]
    public async Task Rescan_removes_deleted_or_untagged_folders()
    {
        using var temp = new TempDir();
        var (scanner, sidecars, db) = Make();
        using var _ = db;

        var folder = temp.CreateFolder("a");
        WriteTags(sidecars, folder, Guid.NewGuid(), "x");
        await scanner.RescanAsync(temp.Path);

        sidecars.Delete(folder);
        var result = await scanner.RescanAsync(temp.Path);

        Assert.Equal(1, result.Removed);
        Assert.Empty(await db.Index.GetAllAsync(temp.Path));
    }

    [Fact]
    public async Task Rescan_reidentifies_duplicate_guids_from_copies()
    {
        using var temp = new TempDir();
        var (scanner, sidecars, db) = Make();
        using var _ = db;

        var sharedId = Guid.NewGuid();
        WriteTags(sidecars, temp.CreateFolder("Original"), sharedId, "док");
        WriteTags(sidecars, temp.CreateFolder("Copy"), sharedId, "док"); // pasted copy → same id

        var result = await scanner.RescanAsync(temp.Path);

        Assert.True(result.Reidentified >= 1);
        Assert.Equal(2, (await db.Index.GetAllAsync(temp.Path)).Count);

        var idA = sidecars.TryRead(Path.Combine(temp.Path, "Original"))!.Id;
        var idB = sidecars.TryRead(Path.Combine(temp.Path, "Copy"))!.Id;
        Assert.NotEqual(idA, idB);
    }

    [Fact]
    public async Task Cover_only_sidecar_is_not_indexed()
    {
        using var temp = new TempDir();
        var (scanner, sidecars, db) = Make();
        using var _ = db;

        var folder = temp.CreateFolder("a");
        sidecars.Write(folder, new Sidecar
        {
            Id = Guid.NewGuid(),
            Tags = [],
            Cover = new CoverInfo { Source = ".tagster_cover.jpg", SetUtc = DateTimeOffset.UnixEpoch },
            UpdatedUtc = DateTimeOffset.UnixEpoch,
        });

        var result = await scanner.RescanAsync(temp.Path);

        Assert.Equal(0, result.TotalIndexed);
        Assert.Empty(await db.Index.GetAllAsync(temp.Path));
    }
}
