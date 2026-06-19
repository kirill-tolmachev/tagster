using System.Collections.Concurrent;
using System.Windows.Media;

namespace Tagster.Shell;

/// <inheritdoc />
public sealed class ShellThumbnailService : IThumbnailService
{
    // Key is "{size}|{path}"; values are frozen, so they are safe to share across threads.
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new();

    public Task<ImageSource?> GetThumbnailAsync(string path, int size, CancellationToken cancellationToken = default)
        => Task.Run<ImageSource?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = $"{size}|{path}";
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var image = NativeThumbnail.TryGetThumbnail(path, size);
            if (image is not null)
                _cache[key] = image; // already frozen by TryGetThumbnail
            return image;
        }, cancellationToken);

    public void Invalidate(string path)
    {
        var suffix = "|" + path;
        foreach (var key in _cache.Keys)
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
        }
    }
}
