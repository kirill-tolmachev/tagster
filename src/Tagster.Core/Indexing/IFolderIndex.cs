namespace Tagster.Core;

/// <summary>
/// The persistent, rebuildable search index over tagged folders (implemented by the SQLite store
/// in Tagster.Data). The sidecars are the source of truth; this index is a disposable cache.
/// </summary>
public interface IFolderIndex
{
    /// <summary>Insert or update a folder and its tags.</summary>
    Task UpsertAsync(TaggedFolder folder, CancellationToken cancellationToken = default);

    /// <summary>Remove a folder (and its tags) by identity.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single folder by identity, or null.</summary>
    Task<TaggedFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All indexed folders under a given archive root.</summary>
    Task<IReadOnlyList<TaggedFolder>> GetAllAsync(string rootPath, CancellationToken cancellationToken = default);

    /// <summary>Folders matching the include/exclude/name query.</summary>
    Task<IReadOnlyList<TaggedFolder>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>Every tag with the number of folders carrying it.</summary>
    Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default);
}
