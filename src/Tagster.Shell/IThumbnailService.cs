using System.Windows.Media;

namespace Tagster.Shell;

/// <summary>Provides Explorer-quality thumbnails for files and folders.</summary>
public interface IThumbnailService
{
    /// <summary>
    /// Returns a frozen thumbnail for <paramref name="path"/> at roughly <paramref name="size"/>
    /// pixels, or null if none is available. Safe to call from any thread.
    /// </summary>
    Task<ImageSource?> GetThumbnailAsync(string path, int size, CancellationToken cancellationToken = default);

    /// <summary>Drop any cached thumbnails for a path (e.g. after its cover changes).</summary>
    void Invalidate(string path);
}
