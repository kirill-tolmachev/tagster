using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Tagster.Shell.NativeFileOperation;

namespace Tagster.Shell;

/// <inheritdoc />
public sealed class FileOperationService(ILogger<FileOperationService>? logger = null) : IFileOperationService
{
    private readonly ILogger _log = logger ?? NullLogger<FileOperationService>.Instance;

    private const FileOpFlags TransferFlags = FileOpFlags.AllowUndo | FileOpFlags.NoConfirmMkDir | FileOpFlags.AddUndoRecord;
    private const FileOpFlags DeleteFlags = FileOpFlags.AllowUndo | FileOpFlags.RecycleOnDelete | FileOpFlags.AddUndoRecord;

    public bool Copy(IReadOnlyList<string> sourcePaths, string destinationFolder, nint ownerWindow)
        => Perform(ownerWindow, TransferFlags, (op, rcws) =>
        {
            var destination = Add(rcws, CreateShellItem(destinationFolder));
            return EnqueueEach(sourcePaths, rcws, item => op.CopyItem(item, destination, null, IntPtr.Zero));
        });

    public bool Move(IReadOnlyList<string> sourcePaths, string destinationFolder, nint ownerWindow)
        => Perform(ownerWindow, TransferFlags, (op, rcws) =>
        {
            var destination = Add(rcws, CreateShellItem(destinationFolder));
            return EnqueueEach(sourcePaths, rcws, item => op.MoveItem(item, destination, null, IntPtr.Zero));
        });

    public bool Delete(IReadOnlyList<string> paths, nint ownerWindow)
        => Perform(ownerWindow, DeleteFlags, (op, rcws) =>
            EnqueueEach(paths, rcws, item => op.DeleteItem(item, IntPtr.Zero)));

    public string? Rename(string path, string newName, nint ownerWindow)
    {
        var completed = Perform(ownerWindow, TransferFlags, (op, rcws) =>
        {
            var item = Add(rcws, CreateShellItem(path));
            Check(op.RenameItem(item, newName, IntPtr.Zero));
            return true;
        });

        if (!completed) return null;
        var parent = Path.GetDirectoryName(path);
        return parent is null ? newName : Path.Combine(parent, newName);
    }

    public string CreateFolder(string parentFolder, string desiredName = "New folder")
    {
        // A plain managed create: no Recycle Bin / conflict concerns, and we want the path back to
        // select-and-rename it. We just replicate Explorer's auto-suffixing for a free name.
        var name = desiredName;
        var target = Path.Combine(parentFolder, name);
        for (var n = 2; Directory.Exists(target) || File.Exists(target); n++)
        {
            name = $"{desiredName} ({n})";
            target = Path.Combine(parentFolder, name);
        }

        Directory.CreateDirectory(target);
        NativeShell.NotifyUpdate(parentFolder);
        return target;
    }

    /// <summary>
    /// Build a single shell operation batch, perform it, and report whether it ran to completion.
    /// <paramref name="build"/> enqueues the per-item calls and returns false when there's nothing to
    /// do. Every COM object created along the way is released in LIFO order afterwards.
    /// </summary>
    private bool Perform(nint ownerWindow, FileOpFlags flags, Func<IFileOperation, List<object>, bool> build)
    {
        var op = CreateFileOperation();
        var rcws = new List<object> { op };
        try
        {
            Check(op.SetOperationFlags((uint)flags));
            if (ownerWindow != 0) op.SetOwnerWindow(ownerWindow);

            if (!build(op, rcws)) return false; // nothing queued

            var hr = op.PerformOperations();
            if (hr == ErrorCancelled) return false;
            Check(hr);

            op.GetAnyOperationsAborted(out var aborted);
            return !aborted;
        }
        finally
        {
            for (var i = rcws.Count - 1; i >= 0; i--)
                if (Marshal.IsComObject(rcws[i])) Marshal.ReleaseComObject(rcws[i]);
        }
    }

    private static bool EnqueueEach(IReadOnlyList<string> paths, List<object> rcws, Action<IShellItem> enqueue)
    {
        var any = false;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            enqueue(Add(rcws, CreateShellItem(path)));
            any = true;
        }
        return any;
    }

    private static IShellItem Add(List<object> rcws, IShellItem item)
    {
        rcws.Add(item);
        return item;
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }
}
