namespace Tagster.Core;

/// <summary>
/// Helpers for converting between absolute paths and portable, '/'-separated paths stored
/// relative to an archive root.
/// </summary>
public static class PathUtil
{
    /// <summary>Path of <paramref name="absolutePath"/> relative to <paramref name="rootPath"/>, using '/'.</summary>
    public static string ToRelative(string rootPath, string absolutePath)
        => Path.GetRelativePath(rootPath, absolutePath).Replace('\\', '/');

    /// <summary>Recompose an absolute path from a root and a '/'-separated relative path.</summary>
    public static string ToAbsolute(string rootPath, string relativePath)
        => Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
