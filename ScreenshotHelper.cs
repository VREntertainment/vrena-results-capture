using System.Drawing.Imaging;

namespace VRenaResultsCapture;

internal static class ScreenshotHelper
{
    internal static Bitmap CaptureScreen(Screen screen)
    {
        var bitmap = new Bitmap(
            screen.Bounds.Width,
            screen.Bounds.Height,
            PixelFormat.Format24bppRgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            screen.Bounds.Left,
            screen.Bounds.Top,
            0,
            0,
            bitmap.Size,
            CopyPixelOperation.SourceCopy);

        return bitmap;
    }

    internal static Bitmap CaptureArea(Screen screen, Rectangle area)
    {
        var normalizedArea = Rectangle.Intersect(
            new Rectangle(Point.Empty, screen.Bounds.Size),
            area);

        if (normalizedArea.Width < 1 || normalizedArea.Height < 1)
        {
            throw new InvalidOperationException("The recognition area is outside the selected display.");
        }

        var bitmap = new Bitmap(
            normalizedArea.Width,
            normalizedArea.Height,
            PixelFormat.Format24bppRgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            screen.Bounds.Left + normalizedArea.Left,
            screen.Bounds.Top + normalizedArea.Top,
            0,
            0,
            bitmap.Size,
            CopyPixelOperation.SourceCopy);

        return bitmap;
    }
}
