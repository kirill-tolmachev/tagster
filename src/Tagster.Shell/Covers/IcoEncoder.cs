using System.IO;
using System.Windows.Media.Imaging;

namespace Tagster.Shell;

/// <summary>
/// Writes a Windows .ico container with one PNG-compressed frame per size. Windows Vista+ reads
/// PNG frames at any size, so a single all-PNG icon covers every display scale.
/// </summary>
internal static class IcoEncoder
{
    public static byte[] Build(IEnumerable<BitmapSource> frames)
    {
        var images = frames.Select(frame => (frame.PixelWidth, frame.PixelHeight, Png: ImageOps.EncodePng(frame))).ToList();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((short)0);              // reserved
        writer.Write((short)1);              // type: 1 = icon
        writer.Write((short)images.Count);   // number of images

        var offset = 6 + 16 * images.Count;  // header + directory entries
        foreach (var (width, height, png) in images)
        {
            writer.Write((byte)(width >= 256 ? 0 : width));
            writer.Write((byte)(height >= 256 ? 0 : height));
            writer.Write((byte)0);   // palette colors
            writer.Write((byte)0);   // reserved
            writer.Write((short)1);  // color planes
            writer.Write((short)32); // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, _, png) in images)
            writer.Write(png);

        writer.Flush();
        return stream.ToArray();
    }
}
