using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tagster.Shell;

/// <summary>
/// Minimal interop over the Windows Shell thumbnail API (<c>IShellItemImageFactory::GetImage</c>),
/// which yields the same images Explorer shows — including custom folder covers.
/// </summary>
internal static class NativeThumbnail
{
    public static ImageSource? TryGetThumbnail(string path, int size)
    {
        object? factoryObject = null;
        nint hBitmap = 0;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, 0, ref iid, out factoryObject) != 0 || factoryObject is null)
                return null;

            var factory = (IShellItemImageFactory)factoryObject;
            var requested = new SIZE { cx = size, cy = size };
            if (factory.GetImage(requested, SIIGBF.ResizeToFit, out hBitmap) != 0 || hBitmap == 0)
                return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); // make it usable from the UI thread after being built on a worker thread
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != 0) DeleteObject(hBitmap);
            if (factoryObject is not null) Marshal.ReleaseComObject(factoryObject);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, nint pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
        ScaleUp = 0x100,
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out nint phbm);
    }
}
