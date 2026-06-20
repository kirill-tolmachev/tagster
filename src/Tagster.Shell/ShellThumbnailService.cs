using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

            // Prefer a folder's own Tagster cover so it shows immediately and isn't subject to
            // Windows' shell thumbnail cache being stale right after the cover is set.
            var image = TryLoadCover(path, size) ?? NativeThumbnail.TryGetThumbnail(path, size);
            if (image is not null)
                _cache[key] = image; // frozen by TryLoadCover / TryGetThumbnail
            return image;
        }, cancellationToken);

    private static ImageSource? TryLoadCover(string folderPath, int size)
    {
        try
        {
            var coverPath = Path.Combine(folderPath, FolderCoverService.CoverSourceName);
            if (!File.Exists(coverPath)) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // IgnoreImageCache: covers always reuse the same file name, so without this WPF would
            // return a previously-decoded image and the tile wouldn't update until the next launch.
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = size;
            image.UriSource = new Uri(coverPath);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

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
