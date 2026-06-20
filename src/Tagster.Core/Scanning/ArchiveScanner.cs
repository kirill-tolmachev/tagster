using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tagster.Core;

/// <summary>
/// Rebuilds/refreshes the index from the sidecars on disk — the operation that makes the archive
/// portable. Reconciles by folder identity (so renames/moves are tracked), and re-identifies the
/// duplicate GUIDs produced when a tagged folder is copy/pasted.
/// </summary>
public sealed class ArchiveScanner(ISidecarStore sidecars, IFolderIndex index, TimeProvider time, ILogger<ArchiveScanner>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<ArchiveScanner>.Instance;

    public async Task<ScanResult> RescanAsync(string rootPath, ScanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ScanOptions();
        rootPath = Path.GetFullPath(rootPath);

        var seenIds = new HashSet<Guid>();
        var desired = new Dictionary<Guid, TaggedFolder>();
        var reidentified = 0;

        foreach (var dir in EnumerateDirectories(rootPath, options.MaxDepth, cancellationToken))
        {
            var sidecar = sidecars.TryRead(dir);
            if (sidecar is null) continue;

            var id = sidecar.Id;
            if (id == Guid.Empty || !seenIds.Add(id))
            {
                // Empty or duplicate identity (e.g. a copy/paste): assign a fresh GUID and persist it.
                id = Guid.NewGuid();
                sidecar = sidecar with { Id = id, UpdatedUtc = time.GetUtcNow() };
                sidecars.Write(dir, sidecar);
                seenIds.Add(id);
                reidentified++;
            }

            var tags = TagNormalizer.NormalizeDisplayList(sidecar.Tags);
            if (tags.Count == 0) continue; // cover-only sidecar; nothing to index

            desired[id] = new TaggedFolder
            {
                Id = id,
                RootPath = rootPath,
                RelativePath = PathUtil.ToRelative(rootPath, dir),
                Name = new DirectoryInfo(dir).Name,
                Tags = tags,
                UpdatedUtc = sidecar.UpdatedUtc,
            };
        }

        var existing = (await index.GetAllAsync(rootPath, cancellationToken)).ToDictionary(f => f.Id);
        int added = 0, updated = 0, removed = 0;

        foreach (var (id, folder) in desired)
        {
            if (existing.TryGetValue(id, out var current))
            {
                if (!SameContent(current, folder))
                {
                    await index.UpsertAsync(folder, cancellationToken);
                    updated++;
                }
            }
            else
            {
                await index.UpsertAsync(folder, cancellationToken);
                added++;
            }
        }

        foreach (var id in existing.Keys)
        {
            if (!desired.ContainsKey(id))
            {
                await index.RemoveAsync(id, cancellationToken);
                removed++;
            }
        }

        return new ScanResult(added, updated, removed, reidentified, desired.Count);
    }

    private static bool SameContent(TaggedFolder a, TaggedFolder b)
    {
        if (!string.Equals(a.RelativePath, b.RelativePath, StringComparison.Ordinal)) return false;
        if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal)) return false;

        var aTags = a.Tags.Select(TagNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);
        var bTags = b.Tags.Select(TagNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);
        return aTags.SetEquals(bTags);
    }

    /// <summary>Depth-bounded directory walk that skips reparse points and unreadable folders.</summary>
    private IEnumerable<string> EnumerateDirectories(string root, int maxDepth, CancellationToken ct)
    {
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (path, depth) = stack.Pop();

            List<string> children;
            try
            {
                children = Directory.EnumerateDirectories(path).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                _log.LogDebug(ex, "Skipping unreadable directory {Path} during scan", path);
                continue;
            }

            foreach (var child in children)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(child);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or FileNotFoundException or IOException)
                {
                    _log.LogDebug(ex, "Skipping folder with unreadable attributes {Child}", child);
                    continue;
                }

                // Skip junctions/symlinks to avoid cycles. Do NOT skip system folders — a folder with
                // a custom cover becomes a system folder, and those must still be scanned.
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;

                yield return child;

                if (depth + 1 < maxDepth)
                    stack.Push((child, depth + 1));
            }
        }
    }
}
