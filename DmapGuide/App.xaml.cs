using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DmapGuide;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Resources["ParchmentNoise"] = CreateParchmentNoise();
        base.OnStartup(e);
    }

    /// <summary>Tileable speckle texture laid over the page colour so it reads as old paper.</summary>
    private static Brush CreateParchmentNoise()
    {
        const int size = 256;
        var rnd = new Random(1213);
        var pixels = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            double r = rnd.NextDouble();
            // Mostly faint grain, with the occasional darker fleck.
            byte alpha = r > 0.995 ? (byte)rnd.Next(60, 110) : (byte)rnd.Next(0, 22);
            // Premultiplied brown (58, 36, 16).
            pixels[i * 4 + 0] = (byte)(16 * alpha / 255);
            pixels[i * 4 + 1] = (byte)(36 * alpha / 255);
            pixels[i * 4 + 2] = (byte)(58 * alpha / 255);
            pixels[i * 4 + 3] = alpha;
        }

        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bmp.Freeze();

        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }
}
