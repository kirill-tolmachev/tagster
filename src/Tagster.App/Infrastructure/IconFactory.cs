using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tagster.Shell;

namespace Tagster.App;

/// <summary>Generates the application icon (a "#" tag glyph on a rounded blue tile).</summary>
internal static class IconFactory
{
    private static readonly int[] Sizes = [256, 128, 64, 48, 32, 16];

    public static void Write(string path)
    {
        var frames = Sizes.Select(Render).ToList();
        File.WriteAllBytes(path, IcoEncoder.Build(frames));
    }

    private static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var radius = size * 0.18;
            var tile = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
            dc.DrawRoundedRectangle(tile, null, new Rect(0, 0, size, size), radius, radius);

            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var glyph = new FormattedText("#", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, size * 0.62, Brushes.White, 1.0);
            dc.DrawText(glyph, new Point((size - glyph.Width) / 2, (size - glyph.Height) / 2));
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
