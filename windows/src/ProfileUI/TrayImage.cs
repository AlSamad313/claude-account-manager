using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProfileUI;

/// The tray icon, as macOS draws its menu-bar icon: a burst on a rounded square in the default
/// profile's colour, so the colour shows which account plain claude uses.
public static class TrayImage
{
    /// Resources/BurstMask.png, shared with macOS: a white burst on transparent.
    public static BitmapSource? BundledMask { get; } = LoadMask();

    static BitmapSource? LoadMask()
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/ProfileUI;component/BurstMask.png");
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException) { return null; }
    }

    public static Color ParseColor(string hex)
    {
        try { if (hex is { Length: 7 } && hex[0] == '#') return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { }
        return (Color)ColorConverter.ConvertFromString(AppModel.Grey);
    }

    /// White, unless the profile's colour is too light for white to show.
    public static Color BurstColor(Color c)
    {
        var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        return luminance > 0.72 ? Color.FromRgb(26, 26, 26) : Colors.White;
    }

    /// Square, size pixels a side, in the same proportions as the macOS 18-point icon.
    public static BitmapSource Render(string hex, int size, BitmapSource? mask)
    {
        var color = ParseColor(hex);
        var unit = size / 18.0;
        var tile = new Rect(unit, unit, size - 2 * unit, size - 2 * unit);
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(color), null, tile, 4 * unit, 4 * unit);
            var burst = new SolidColorBrush(BurstColor(color));
            if (mask != null)
            {
                var inner = Rect.Inflate(tile, -unit, -unit);
                dc.PushOpacityMask(new ImageBrush(mask) { Viewport = inner, ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill });
                dc.DrawRectangle(burst, null, inner);
                dc.Pop();
            }
            else
            {
                // Without the mask, the colour still shows the default.
                var r = (tile.Width - 10 * unit) / 2;
                dc.DrawEllipse(burst, null, new Point(size / 2.0, size / 2.0), r, r);
            }
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v);
        bmp.Freeze();
        return bmp;
    }

    /// The colour at a pixel, for tests.
    public static Color Pixel(BitmapSource b, int x, int y)
    {
        var px = new byte[4];
        b.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return Color.FromArgb(px[3], px[2], px[1], px[0]);
    }
}
