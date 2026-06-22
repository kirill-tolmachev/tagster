using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace Tagster.App;

/// <summary>
/// Reads/writes the file clipboard the way Explorer does: a <c>CF_HDROP</c> file list plus the
/// <c>"Preferred DropEffect"</c> format that distinguishes a cut (move) from a copy. Setting both is
/// what lets a copy/cut here paste into Explorer — and vice-versa. Must be called on the STA UI thread.
/// </summary>
internal static class ClipboardFiles
{
    private const string PreferredDropEffect = "Preferred DropEffect";

    public static void Set(IReadOnlyList<string> paths, bool cut)
    {
        if (paths.Count == 0) return;

        var list = new StringCollection();
        foreach (var path in paths) list.Add(path);

        var data = new DataObject();
        data.SetFileDropList(list);
        // DROPEFFECT_MOVE = 2, DROPEFFECT_COPY = 1 — a 4-byte DWORD the shell reads back.
        var effect = cut ? DragDropEffects.Move : DragDropEffects.Copy;
        data.SetData(PreferredDropEffect, new MemoryStream(BitConverter.GetBytes((uint)effect)));

        try
        {
            Clipboard.SetDataObject(data, copy: true); // flush so it survives after we exit
        }
        catch (Exception)
        {
            // The clipboard can be transiently locked by another process; just drop the request.
        }
    }

    /// <summary>True if the clipboard currently holds a file drop list we could paste.</summary>
    public static bool HasFiles()
    {
        try { return Clipboard.ContainsFileDropList(); }
        catch (Exception) { return false; }
    }

    /// <summary>Read the clipboard's file list and whether it was a cut (move) rather than a copy.</summary>
    public static bool TryGet(out IReadOnlyList<string> paths, out bool isMove)
    {
        paths = [];
        isMove = false;
        try
        {
            if (!Clipboard.ContainsFileDropList()) return false;

            var files = Clipboard.GetFileDropList();
            var list = new List<string>(files.Count);
            foreach (var file in files)
                if (!string.IsNullOrEmpty(file)) list.Add(file);
            if (list.Count == 0) return false;
            paths = list;

            if (Clipboard.GetDataObject()?.GetData(PreferredDropEffect) is MemoryStream stream && stream.Length >= 4)
            {
                Span<byte> buffer = stackalloc byte[4];
                stream.Position = 0;
                if (stream.Read(buffer) == 4)
                    isMove = ((DragDropEffects)BitConverter.ToUInt32(buffer)).HasFlag(DragDropEffects.Move);
            }
            return true;
        }
        catch (Exception)
        {
            paths = [];
            return false;
        }
    }

    public static void Clear()
    {
        try { Clipboard.Clear(); }
        catch (Exception) { /* best effort */ }
    }
}
