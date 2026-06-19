namespace Tagster.Shell;

/// <summary>Sets and clears a folder's custom cover icon from within the app.</summary>
public interface IFolderCoverService
{
    /// <summary>
    /// Generate a multi-resolution icon from <paramref name="sourceImagePath"/> and apply it as
    /// <paramref name="folderPath"/>'s cover (via desktop.ini). Returns the folder-relative file
    /// name of the stored cover source, for recording in the sidecar.
    /// </summary>
    string SetCover(string folderPath, string sourceImagePath);

    /// <summary>Remove a folder's custom cover and the files Tagster wrote for it.</summary>
    void RemoveCover(string folderPath);
}
