using System.Runtime.InteropServices;

namespace Tagster.Shell;

/// <summary>Shell change notifications so Explorer (and our own thumbnails) pick up a new cover.</summary>
internal static class NativeShell
{
    private const uint SHCNE_UPDATEITEM = 0x00002000;
    private const uint SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_PATHW = 0x0005;

    public static void NotifyUpdate(string path)
    {
        var item = Marshal.StringToHGlobalUni(path);
        try
        {
            SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, item, IntPtr.Zero);
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, item, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(item);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
