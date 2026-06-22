using System.Text;

namespace Tagster.Core;

/// <summary>
/// Crash-safe, atomic file replacement. Bytes are streamed into a uniquely-named temp file in the
/// SAME directory and flushed all the way to disk, then swapped over the target with
/// <see cref="File.Move(string, string, bool)"/>. A reader therefore only ever sees the old complete
/// file or the new complete file — never a truncated or half-written one, even across a crash or
/// power loss. The temp carries the final attributes (so it never shows up mid-write) and is always
/// cleaned up on failure; a failed replace leaves the original file and its attributes intact.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Atomically write <paramref name="contents"/> to <paramref name="path"/>, giving the final file
    /// <paramref name="attributes"/> (e.g. <see cref="FileAttributes.Hidden"/>, or Hidden | System).
    /// Use <see cref="FileAttributes.Normal"/> for an ordinary file.
    /// </summary>
    public static void Write(string path, byte[] contents, FileAttributes attributes = FileAttributes.Normal)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";

        // Remember the existing file's attributes so a failed replace can leave it exactly as it was.
        var hadExisting = File.Exists(path);
        var existingAttributes = hadExisting ? File.GetAttributes(path) : FileAttributes.Normal;

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents, 0, contents.Length);
                // Force the bytes to physical disk BEFORE the rename, so a crash/power-loss can't
                // commit the new file name over still-buffered (empty or partial) data.
                stream.Flush(flushToDisk: true);
            }

            // Carry the final attributes on the temp so they survive the move and the temp is never
            // visible (e.g. in the folder grid) during its brief life. Cosmetic, so never fatal.
            TrySetAttributes(temp, attributes);

            // Clearing an existing target's attributes first means the replace can't fail on a hidden
            // or system destination (a long-standing Windows MoveFileEx quirk).
            if (hadExisting)
                File.SetAttributes(path, FileAttributes.Normal);

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // The move never completed, so the original (if any) survives — clean up the temp and
            // restore the original's attributes so a failed write changes nothing observable.
            TryDelete(temp);
            if (hadExisting && File.Exists(path))
                TrySetAttributes(path, existingAttributes);
            throw;
        }
    }

    /// <summary>Atomically write <paramref name="contents"/> as text encoded with <paramref name="encoding"/>.</summary>
    public static void Write(string path, string contents, Encoding encoding,
        FileAttributes attributes = FileAttributes.Normal)
        => Write(path, encoding.GetBytes(contents), attributes);

    private static void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal); // drop Hidden/System/ReadOnly so Delete isn't blocked
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TrySetAttributes(string path, FileAttributes attributes)
    {
        try { File.SetAttributes(path, attributes); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
