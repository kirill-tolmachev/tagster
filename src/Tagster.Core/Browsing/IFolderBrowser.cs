namespace Tagster.Core;

/// <summary>Lists the contents (child folders and files) of a directory for the browser view.</summary>
public interface IFolderBrowser
{
    /// <summary>
    /// Immediate contents of <paramref name="path"/>, sorted for display: child folders first
    /// (each with its tag state), then files. Hidden/system entries are omitted.
    /// </summary>
    IReadOnlyList<FolderEntry> ListEntries(string path);
}
