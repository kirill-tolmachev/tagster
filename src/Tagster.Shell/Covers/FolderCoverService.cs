using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace Tagster.Shell;

/// <inheritdoc />
public sealed class FolderCoverService : IFolderCoverService
{
    /// <summary>Icon Windows displays for the folder.</summary>
    public const string IconFileName = "Tagster.ico";

    /// <summary>Portable, hidden source image kept so the cover can be re-generated and travels.</summary>
    public const string CoverSourceName = ".tagster_cover.png";

    private const string DesktopIniName = "desktop.ini";
    private const int SourceSize = 512;
    private static readonly int[] IconSizes = [256, 128, 64, 48, 32, 16];

    public string SetCover(string folderPath, string sourceImagePath)
    {
        var square = ImageOps.LoadSquare(sourceImagePath);

        // Keep a compact square source (portable + re-generatable).
        WriteHidden(Path.Combine(folderPath, CoverSourceName), ImageOps.EncodePng(ImageOps.RenderAt(square, SourceSize)));

        // The multi-resolution icon Windows actually shows.
        var frames = IconSizes.Select(size => ImageOps.RenderAt(square, size));
        WriteHidden(Path.Combine(folderPath, IconFileName), IcoEncoder.Build(frames));

        WriteDesktopIni(folderPath);
        MarkSystem(folderPath);
        NativeShell.NotifyUpdate(folderPath);

        return CoverSourceName;
    }

    public void RemoveCover(string folderPath)
    {
        foreach (var name in new[] { IconFileName, CoverSourceName, DesktopIniName })
            TryDelete(Path.Combine(folderPath, name));

        try
        {
            new DirectoryInfo(folderPath).Attributes &= ~FileAttributes.System;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // leave attributes as-is if we can't change them
        }

        NativeShell.NotifyUpdate(folderPath);
    }

    private static void WriteDesktopIni(string folderPath)
    {
        var path = Path.Combine(folderPath, DesktopIniName);
        var content = new StringBuilder()
            .AppendLine("[.ShellClassInfo]")
            .AppendLine($"IconResource={IconFileName},0")
            .AppendLine("ConfirmFileOp=0")
            .ToString();

        ClearAttributes(path);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        SetHiddenSystem(path);
    }

    private static void WriteHidden(string path, byte[] bytes)
    {
        ClearAttributes(path);
        File.WriteAllBytes(path, bytes);
        SetHiddenSystem(path);
    }

    private static void MarkSystem(string folderPath)
    {
        // A folder must be system (or read-only) for Windows to honour its desktop.ini.
        try { new DirectoryInfo(folderPath).Attributes |= FileAttributes.System; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void SetHiddenSystem(string path)
    {
        try { File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void ClearAttributes(string path)
    {
        try { if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
