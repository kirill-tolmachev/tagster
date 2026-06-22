namespace Tagster.Shell;

/// <summary>How far a shell file operation got, so callers know whether the disk changed.</summary>
public enum FileOpResult
{
    /// <summary>Nothing was performed — no valid items, or the user cancelled before any work ran.</summary>
    NothingDone,

    /// <summary>The whole batch ran with no aborted items.</summary>
    Completed,

    /// <summary>
    /// The operation ran but the user cancelled/aborted partway, so some items may already have been
    /// moved/copied/recycled. Callers must reconcile (e.g. rescan) as if it had completed.
    /// </summary>
    Partial,
}

/// <summary>
/// File-manager operations backed by the Windows shell <c>IFileOperation</c> API — the same engine
/// Explorer uses, so callers get its progress dialog, "merge folder?" / overwrite prompts, Recycle
/// Bin, and undo for free. The shell also raises its own change notifications, so Explorer and our
/// thumbnails refresh without extra work.
/// </summary>
/// <remarks>
/// These call modal shell UI and must run on the UI (STA) thread; the progress dialog pumps messages
/// so the window stays responsive during long copies. <paramref name="ownerWindow"/> is the HWND the
/// shell dialogs are parented to (0 for none).
/// </remarks>
public interface IFileOperationService
{
    /// <summary>Copy items into <paramref name="destinationFolder"/>.</summary>
    FileOpResult Copy(IReadOnlyList<string> sourcePaths, string destinationFolder, nint ownerWindow);

    /// <summary>Move items into <paramref name="destinationFolder"/>.</summary>
    FileOpResult Move(IReadOnlyList<string> sourcePaths, string destinationFolder, nint ownerWindow);

    /// <summary>Send items to the Recycle Bin.</summary>
    FileOpResult Delete(IReadOnlyList<string> paths, nint ownerWindow);

    /// <summary>Rename a single item in place; returns its new full path, or null if cancelled.</summary>
    string? Rename(string path, string newName, nint ownerWindow);

    /// <summary>
    /// Create a new subfolder under <paramref name="parentFolder"/>, auto-suffixing the name to avoid
    /// a collision ("New folder", "New folder (2)", …). Returns the created folder's full path.
    /// </summary>
    string CreateFolder(string parentFolder, string desiredName = "New folder");
}
