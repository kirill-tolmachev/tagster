using Tagster.Core;

namespace Tagster.Tests;

/// <summary>End-to-end exercise of the pipeline the M3 UI drives: scan → search → manage.</summary>
public class IntegrationFlowTests
{
    [Fact]
    public async Task Scan_then_search_include_exclude_then_rename_and_delete()
    {
        using var temp = new TempDir();
        using var db = new TempDb();
        var sidecars = new SidecarStore();
        var time = new StubTimeProvider(DateTimeOffset.UnixEpoch);
        var scanner = new ArchiveScanner(sidecars, db.Index, time);
        var manager = new TagManager(sidecars, db.Index, time);

        // An archive of author folders, tags written only as sidecars (as if from another machine).
        var ivanov = temp.CreateFolder("Иванов");
        var petrov = temp.CreateFolder("Петров");
        var sidorov = temp.CreateFolder("Сидоров");
        sidecars.Write(ivanov, new Sidecar { Id = Guid.NewGuid(), Tags = ["док", "война"], UpdatedUtc = DateTimeOffset.UnixEpoch });
        sidecars.Write(petrov, new Sidecar { Id = Guid.NewGuid(), Tags = ["док", "портрет"], UpdatedUtc = DateTimeOffset.UnixEpoch });
        sidecars.Write(sidorov, new Sidecar { Id = Guid.NewGuid(), Tags = ["портрет"], UpdatedUtc = DateTimeOffset.UnixEpoch });

        // Scan rebuilds the index from the sidecars (the portability path).
        Assert.Equal(3, (await scanner.RescanAsync(temp.Path)).TotalIndexed);

        // Tag counts are scoped to this root.
        Assert.Equal(2, (await db.Index.GetTagCountsAsync(temp.Path)).Single(c => c.Name == "док").Count);

        // "док" AND NOT "война" → only Петров.
        var filtered = await db.Index.SearchAsync(new SearchQuery { Include = ["док"], Exclude = ["война"] }, temp.Path);
        Assert.Equal(new[] { "Петров" }, filtered.Select(f => f.Name));

        // Rename война → конфликт; the new name finds Иванов, the old one finds nothing.
        await manager.RenameAsync(temp.Path, "война", "конфликт");
        Assert.Single(await db.Index.SearchAsync(new SearchQuery { Include = ["конфликт"] }, temp.Path));
        Assert.Empty(await db.Index.SearchAsync(new SearchQuery { Include = ["война"] }, temp.Path));

        // Delete портрет → Сидоров (portrait-only) drops out of the index and loses its sidecar.
        await manager.DeleteAsync(temp.Path, "портрет");
        Assert.Empty(await db.Index.SearchAsync(new SearchQuery { Include = ["портрет"] }, temp.Path));
        Assert.Null(sidecars.TryRead(sidorov));
    }
}
