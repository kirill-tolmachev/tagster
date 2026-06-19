namespace Tagster.Core;

/// <summary>
/// Coordinates a single tagging operation: it writes the per-folder sidecar (source of truth)
/// and keeps the search index in sync. Folder identity is preserved across edits via the sidecar.
/// </summary>
public sealed class TaggingService(ISidecarStore sidecars, IFolderIndex index, TimeProvider time)
{
    /// <summary>Current display tags for a folder (read from its sidecar).</summary>
    public IReadOnlyList<string> GetTags(string folderAbsolutePath)
        => sidecars.TryRead(folderAbsolutePath)?.Tags ?? [];

    /// <summary>Add tags to a folder, keeping any it already has.</summary>
    public Task AddTagsAsync(string rootPath, string folderAbsolutePath, IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
        => SetTagsAsync(rootPath, folderAbsolutePath, GetTags(folderAbsolutePath).Concat(tags), cancellationToken);

    /// <summary>Remove tags from a folder (case-insensitive); others are kept.</summary>
    public Task RemoveTagsAsync(string rootPath, string folderAbsolutePath, IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
    {
        var remove = tags.Select(TagNormalizer.Normalize)
            .Where(static n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var kept = GetTags(folderAbsolutePath)
            .Where(t => !remove.Contains(TagNormalizer.Normalize(t)));
        return SetTagsAsync(rootPath, folderAbsolutePath, kept, cancellationToken);
    }

    /// <summary>Replace a folder's tags wholesale, syncing both the sidecar and the index.</summary>
    public async Task SetTagsAsync(string rootPath, string folderAbsolutePath, IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
    {
        var display = TagNormalizer.NormalizeDisplayList(tags);
        var existing = sidecars.TryRead(folderAbsolutePath);
        var id = existing?.Id ?? Guid.NewGuid();
        var now = time.GetUtcNow();

        // No tags and no cover → drop the sidecar and de-index the folder entirely.
        if (display.Count == 0 && existing?.Cover is null)
        {
            sidecars.Delete(folderAbsolutePath);
            await index.RemoveAsync(id, cancellationToken);
            return;
        }

        sidecars.Write(folderAbsolutePath, new Sidecar
        {
            Version = 1,
            Id = id,
            Tags = display,
            Cover = existing?.Cover,
            UpdatedUtc = now,
        });

        // A cover-only folder stays on disk but carries nothing to search.
        if (display.Count == 0)
        {
            await index.RemoveAsync(id, cancellationToken);
            return;
        }

        await index.UpsertAsync(new TaggedFolder
        {
            Id = id,
            RootPath = rootPath,
            RelativePath = PathUtil.ToRelative(rootPath, folderAbsolutePath),
            Name = new DirectoryInfo(folderAbsolutePath).Name,
            Tags = display,
            UpdatedUtc = now,
        }, cancellationToken);
    }

    /// <summary>Record (or replace) a folder's cover in its sidecar, preserving its tags.</summary>
    public void SetCover(string folderAbsolutePath, string coverSource)
    {
        var now = time.GetUtcNow();
        var cover = new CoverInfo { Source = coverSource, SetUtc = now };
        var existing = sidecars.TryRead(folderAbsolutePath);
        sidecars.Write(folderAbsolutePath, existing is null
            ? new Sidecar { Id = Guid.NewGuid(), Tags = [], Cover = cover, UpdatedUtc = now }
            : existing with { Cover = cover, UpdatedUtc = now });
    }

    /// <summary>Clear a folder's cover; removes the sidecar entirely if no tags remain.</summary>
    public void RemoveCover(string folderAbsolutePath)
    {
        var existing = sidecars.TryRead(folderAbsolutePath);
        if (existing is null) return;

        if (existing.Tags.Count == 0)
        {
            sidecars.Delete(folderAbsolutePath);
            return;
        }
        sidecars.Write(folderAbsolutePath, existing with { Cover = null, UpdatedUtc = time.GetUtcNow() });
    }
}
