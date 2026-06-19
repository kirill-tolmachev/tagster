using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tagster.Shell;

/// <summary>WPF imaging helpers used to build folder cover icons.</summary>
internal static class ImageOps
{
    /// <summary>Load an image and center-crop it to a square.</summary>
    public static BitmapSource LoadSquare(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();

        var side = Math.Min(image.PixelWidth, image.PixelHeight);
        var x = (image.PixelWidth - side) / 2;
        var y = (image.PixelHeight - side) / 2;
        var cropped = new CroppedBitmap(image, new Int32Rect(x, y, side, side));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>Render a source square into a crisp size×size bitmap.</summary>
    public static BitmapSource RenderAt(BitmapSource source, int size)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
            dc.DrawImage(source, new Rect(0, 0, size, size));

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
