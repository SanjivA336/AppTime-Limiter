using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Curfew.App.Services;

/// <summary>Development aid: captures a window to a PNG so the UI can be reviewed without looking at a screen.</summary>
public static class ScreenshotService
{
    /// <summary>
    /// Draws the window's own contents straight to a PNG, without looking at the screen. It doesn't depend on the
    /// window being in front, and it can't pick up anything else the user has open. The frosted (Mica) background
    /// isn't part of this, so the image is put on a plain dark background instead.
    /// </summary>
    public static void SaveOffscreen(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);

        var drawn = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        drawn.Render(window);

        var area = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
        var withBackground = new DrawingVisual();
        using (var context = withBackground.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x28, 0x24, 0x26)), null, area);
            context.DrawImage(drawn, area);
        }

        var final = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        final.Render(withBackground);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>
    /// Copies the window's area from the real screen, so the Mica backdrop and theme appear exactly as a user sees them.
    /// (Rendering the visual tree offscreen would leave out the backdrop.)
    /// </summary>
    public static void Save(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var left = (int)Math.Round(window.Left * dpi.DpiScaleX);
        var top = (int)Math.Round(window.Top * dpi.DpiScaleY);

        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
