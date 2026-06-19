namespace Tagster.Core;

/// <summary>Bulk tag operations across an archive: rename (including merge) and delete.</summary>
public interface ITagManager
{
    /// <summary>
    /// Rename <paramref name="fromTag"/> to <paramref name="toTag"/> across all tagged folders under
    /// <paramref name="rootPath"/>. If a folder already carries the target, the tags merge
    /// (de-duplicated). Returns the number of folders changed.
    /// </summary>
    Task<int> RenameAsync(string rootPath, string fromTag, string toTag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove <paramref name="tag"/> from every folder under <paramref name="rootPath"/>. A folder
    /// left with no tags (and no cover) is dropped entirely. Returns the number of folders changed.
    /// </summary>
    Task<int> DeleteAsync(string rootPath, string tag, CancellationToken cancellationToken = default);
}
