using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tagster.Shell;

/// <summary>WPF imaging helpers used to build folder cover icons.</summary>
internal static class ImageOps
{
    /// <summary>Load an image, honour its EXIF orientation, and center-crop it to a square.</summary>
    public static BitmapSource LoadSquare(string path)
    {
        var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        var image = ApplyOrientation(frame);

        var side = Math.Min(image.PixelWidth, image.PixelHeight);
        var x = (image.PixelWidth - side) / 2;
        var y = (image.PixelHeight - side) / 2;
        var cropped = new CroppedBitmap(image, new Int32Rect(x, y, side, side));
        cropped.Freeze();
        return cropped;
    }

    private static BitmapSource ApplyOrientation(BitmapFrame frame)
    {
        Transform? transform = ReadOrientation(frame) switch
        {
            3 => new RotateTransform(180),
            6 => new RotateTransform(90),
            8 => new RotateTransform(270),
            _ => null,
        };

        if (transform is null)
        {
            frame.Freeze();
            return frame;
        }

        var rotated = new TransformedBitmap(frame, transform);
        rotated.Freeze();
        return rotated;
    }

    private static ushort ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata
                && metadata.ContainsQuery("System.Photo.Orientation")
                && metadata.GetQuery("System.Photo.Orientation") is { } value)
            {
                return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // unreadable or missing orientation → treat as upright
        }
        return 1;
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
