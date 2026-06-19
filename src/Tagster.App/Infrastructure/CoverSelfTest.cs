using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tagster.Shell;

namespace Tagster.App;

/// <summary>
/// Headless check (invoked via <c>--cover-test</c>) that exercises the real cover pipeline on a
/// temp folder and validates the generated icon, desktop.ini, and folder attribute.
/// </summary>
internal static class CoverSelfTest
{
    public static (bool Ok, string Message) Run(IFolderCoverService covers)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tagster-cover-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var imagePath = Path.Combine(dir, "source.png");
            File.WriteAllBytes(imagePath, MakeTestPng(640, 480));

            var source = covers.SetCover(dir, imagePath);

            var icoPath = Path.Combine(dir, FolderCoverService.IconFileName);
            var iniPath = Path.Combine(dir, "desktop.ini");

            if (!File.Exists(icoPath)) return (false, "FAIL: icon not created");
            if (!File.Exists(iniPath)) return (false, "FAIL: desktop.ini not created");
            if (!File.ReadAllText(iniPath).Contains("IconResource=" + FolderCoverService.IconFileName))
                return (false, "FAIL: desktop.ini missing IconResource");

            var decoder = BitmapDecoder.Create(new Uri(icoPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count < 3) return (false, $"FAIL: icon has only {decoder.Frames.Count} frame(s)");

            if (!new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.System))
                return (false, "FAIL: folder not marked system");

            covers.RemoveCover(dir);
            if (File.Exists(icoPath) || File.Exists(iniPath))
                return (false, "FAIL: cover files not removed");

            return (true, $"PASS: cover applied with {decoder.Frames.Count} icon frames (source={source}) and removed cleanly");
        }
        catch (Exception ex)
        {
            return (false, "FAIL: " + ex);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static byte[] MakeTestPng(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.SteelBlue, null, new Rect(0, 0, width, height));
            dc.DrawEllipse(Brushes.Gold, null, new Point(width / 2.0, height / 2.0), width / 4.0, height / 4.0);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
