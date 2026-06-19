namespace Tagster.Core;

/// <summary>Lists the child folders of a directory for the browser view.</summary>
public interface IFolderBrowser
{
    /// <summary>Immediate sub-folders of <paramref name="path"/>, sorted for display.</summary>
    IReadOnlyList<FolderEntry> ListFolders(string path);
}
