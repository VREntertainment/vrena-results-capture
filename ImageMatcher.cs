using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VRenaResultsCapture;

internal static class ImageMatcher
{
    private const int MaximumWidth = 180;
    private const int MaximumHeight = 90;

    internal static double Compare(Bitmap reference, Bitmap candidate)
    {
        var targetSize = GetComparisonSize(reference.Size);
        using var normalizedReference = Resize(reference, targetSize);
        using var normalizedCandidate = Resize(candidate, targetSize);

        var first = ReadGrayscale(normalizedReference);
        var second = ReadGrayscale(normalizedCandidate);
        return NormalizedCorrelation(first, second);
    }

    private static Size GetComparisonSize(Size source)
    {
        var scale = Math.Min(
            1d,
            Math.Min((double)MaximumWidth / source.Width, (double)MaximumHeight / source.Height));

        return new Size(
            Math.Max(8, (int)Math.Round(source.Width * scale)),
            Math.Max(8, (int)Math.Round(source.Height * scale)));
    }

    private static Bitmap Resize(Bitmap source, Size size)
    {
        var result = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        graphics.DrawImage(source, new Rectangle(Point.Empty, size));
        return result;
    }

    private static double[] ReadGrayscale(Bitmap bitmap)
    {
        var rectangle = new Rectangle(Point.Empty, bitmap.Size);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var pixels = new double[data.Width * data.Height];

            for (var y = 0; y < data.Height; y++)
            {
                var rowOffset = y * data.Stride;
                for (var x = 0; x < data.Width; x++)
                {
                    var offset = rowOffset + x * 3;
                    var blue = bytes[offset];
                    var green = bytes[offset + 1];
                    var red = bytes[offset + 2];
                    pixels[y * data.Width + x] = 0.114 * blue + 0.587 * green + 0.299 * red;
                }
            }

            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static double NormalizedCorrelation(double[] first, double[] second)
    {
        if (first.Length != second.Length || first.Length == 0)
        {
            return 0;
        }

        var firstMean = first.Average();
        var secondMean = second.Average();
        double numerator = 0;
        double firstEnergy = 0;
        double secondEnergy = 0;

        for (var index = 0; index < first.Length; index++)
        {
            var firstCentered = first[index] - firstMean;
            var secondCentered = second[index] - secondMean;
            numerator += firstCentered * secondCentered;
            firstEnergy += firstCentered * firstCentered;
            secondEnergy += secondCentered * secondCentered;
        }

        var denominator = Math.Sqrt(firstEnergy * secondEnergy);
        if (denominator < 0.0001)
        {
            return 0;
        }

        return Math.Clamp((numerator / denominator + 1d) / 2d, 0d, 1d);
    }
}
