namespace Tagster.Core;

/// <inheritdoc />
public sealed class TagManager(ISidecarStore sidecars, IFolderIndex index, TimeProvider time) : ITagManager
{
    public async Task<int> RenameAsync(string rootPath, string fromTag, string toTag,
        CancellationToken cancellationToken = default)
    {
        var fromNorm = TagNormalizer.Normalize(fromTag);
        var toNorm = TagNormalizer.Normalize(toTag);
        if (fromNorm.Length == 0 || toNorm.Length == 0 || fromNorm == toNorm)
            return 0;

        var affected = 0;
        var folders = await index.SearchAsync(new SearchQuery { Include = [fromTag] }, rootPath, cancellationToken);
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = folder.AbsolutePath;
            var sidecar = sidecars.TryRead(path);
            if (sidecar is null) continue;

            var replaced = sidecar.Tags.Select(t => TagNormalizer.Normalize(t) == fromNorm ? toTag : t);
            var tags = TagNormalizer.NormalizeDisplayList(replaced);

            sidecars.Write(path, sidecar with { Tags = tags, UpdatedUtc = time.GetUtcNow() });
            await index.UpsertAsync(folder with { Tags = tags, UpdatedUtc = time.GetUtcNow() }, cancellationToken);
            affected++;
        }
        return affected;
    }

    public async Task<int> DeleteAsync(string rootPath, string tag, CancellationToken cancellationToken = default)
    {
        var norm = TagNormalizer.Normalize(tag);
        if (norm.Length == 0) return 0;

        var affected = 0;
        var folders = await index.SearchAsync(new SearchQuery { Include = [tag] }, rootPath, cancellationToken);
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = folder.AbsolutePath;
            var sidecar = sidecars.TryRead(path);
            if (sidecar is null) continue;

            var remaining = TagNormalizer.NormalizeDisplayList(
                sidecar.Tags.Where(t => TagNormalizer.Normalize(t) != norm));

            if (remaining.Count == 0 && sidecar.Cover is null)
            {
                sidecars.Delete(path);
                await index.RemoveAsync(folder.Id, cancellationToken);
            }
            else if (remaining.Count == 0)
            {
                // Keep the cover-only sidecar, but drop the folder from the search index.
                sidecars.Write(path, sidecar with { Tags = remaining, UpdatedUtc = time.GetUtcNow() });
                await index.RemoveAsync(folder.Id, cancellationToken);
            }
            else
            {
                sidecars.Write(path, sidecar with { Tags = remaining, UpdatedUtc = time.GetUtcNow() });
                await index.UpsertAsync(folder with { Tags = remaining, UpdatedUtc = time.GetUtcNow() }, cancellationToken);
            }
            affected++;
        }
        return affected;
    }
}
