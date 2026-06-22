using System.IO;
using System.Runtime.InteropServices;

namespace Tagster.Shell;

/// <summary>
/// Minimal interop over the Windows shell <c>IFileOperation</c> (Vista+) and <c>IShellItem</c>. The
/// vtable layout must be declared in full and in order, so every method is present even where unused;
/// only the ones we call carry meaningful signatures.
/// </summary>
internal static class NativeFileOperation
{
    /// <summary>Subset of FOF_*/FOFX_* operation flags we set.</summary>
    [Flags]
    public enum FileOpFlags : uint
    {
        Silent = 0x0004,            // FOF_SILENT
        NoConfirmation = 0x0010,    // FOF_NOCONFIRMATION (assume "yes to all")
        AllowUndo = 0x0040,         // FOF_ALLOWUNDO (Recycle Bin on delete; undo for move/copy)
        NoConfirmMkDir = 0x0200,    // FOF_NOCONFIRMMKDIR
        RecycleOnDelete = 0x00080000, // FOFX_RECYCLEONDELETE (Win8+): force Recycle Bin
        AddUndoRecord = 0x20000000, // FOFX_ADDUNDORECORD (Win8+): richer undo
    }

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED) — the user dismissed/cancelled the operation.</summary>
    public const int ErrorCancelled = unchecked((int)0x800704C7);

    public static IFileOperation CreateFileOperation()
    {
        var type = Type.GetTypeFromCLSID(CLSID_FileOperation)
            ?? throw new InvalidOperationException("The FileOperation COM class is unavailable.");
        return (IFileOperation)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Could not create a FileOperation instance."));
    }

    public static IShellItem CreateShellItem(string path)
    {
        var iid = typeof(IShellItem).GUID;
        var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
        if (hr != 0 || item is null)
            throw Marshal.GetExceptionForHR(hr) ?? new IOException($"Cannot resolve a shell item for '{path}'.");
        return (IShellItem)item;
    }

    private static readonly Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr pfops, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOperationFlags(uint dwOperationFlags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        [PreserveSig] int SetProgressDialog(IntPtr popd);
        [PreserveSig] int SetProperties(IntPtr pproparray);
        [PreserveSig] int SetOwnerWindow(IntPtr hwndOwner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem psiItem);
        [PreserveSig] int ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
        [PreserveSig] int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        [PreserveSig] int RenameItems([MarshalAs(UnmanagedType.IUnknown)] object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr pfopsItem);
        [PreserveSig] int MoveItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
        [PreserveSig] int CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IntPtr pfopsItem);
        [PreserveSig] int CopyItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
        [PreserveSig] int DeleteItem(IShellItem psiItem, IntPtr pfopsItem);
        [PreserveSig] int DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
        [PreserveSig] int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IntPtr pfopsItem);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
