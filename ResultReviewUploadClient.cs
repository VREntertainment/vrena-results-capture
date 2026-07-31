using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;

namespace VRenaResultsCapture;

internal static class ResultReviewUploadClient
{
    private const long MaximumScreenshotBytes = 2_000_000;
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    internal static async Task<ResultReviewUploadReceipt> UploadAsync(
        CaptureSettings settings,
        PendingResultReview review)
    {
        ValidateSettings(settings);
        if (!File.Exists(review.ScreenshotPath))
        {
            throw new InvalidOperationException("The review screenshot is missing.");
        }

        var screenshotBytes = CreateReviewJpeg(review.ScreenshotPath);
        if (screenshotBytes.Length > MaximumScreenshotBytes)
        {
            throw new InvalidOperationException(
                $"The review screenshot is too large to upload ({screenshotBytes.Length / 1_000_000d:0.0} MB).");
        }

        var endpoint = $"{settings.WebAppBaseUrl.Trim().TrimEnd('/')}/api/venue/results/review";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.IngestToken.Trim());
        request.Headers.Add("X-VRena-Device-Name", Environment.MachineName);
        request.Headers.Add(
            "X-VRena-App-Version",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Application.ProductVersion);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(review.CaptureId, Encoding.UTF8), "captureId");
        form.Add(new StringContent(review.CapturedAt.ToString("O"), Encoding.UTF8), "capturedAt");
        form.Add(new StringContent(review.ReviewReason, Encoding.UTF8), "reviewReason");
        form.Add(new StringContent(review.OcrText, Encoding.UTF8), "ocrText");
        using var screenshotContent = new ByteArrayContent(screenshotBytes);
        screenshotContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(screenshotContent, "screenshot", $"{review.CaptureId}.jpg");
        request.Content = form;

        using var response = await Client.SendAsync(request);
        DiagnosticLog.Info(
            $"Result review upload returned HTTP {(int)response.StatusCode}. CaptureId={review.CaptureId}");
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Result review upload failed ({(int)response.StatusCode}). {TrimResponse(detail)}");
        }

        return await response.Content.ReadFromJsonAsync<ResultReviewUploadReceipt>()
            ?? throw new InvalidOperationException("The result review upload receipt is invalid.");
    }

    private static byte[] CreateReviewJpeg(string screenshotPath)
    {
        using var source = new Bitmap(screenshotPath);
        const int maximumWidth = 1920;
        const int maximumHeight = 1080;
        var scale = Math.Min(
            1d,
            Math.Min(
                (double)maximumWidth / source.Width,
                (double)maximumHeight / source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        var encoder = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var quality = new EncoderParameters(1);
        quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 78L);
        using var stream = new MemoryStream();
        resized.Save(stream, encoder, quality);
        return stream.ToArray();
    }

    private static void ValidateSettings(CaptureSettings settings)
    {
        if (!Uri.TryCreate(settings.WebAppBaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Enter a valid HTTPS web app URL.");
        }

        if (settings.IngestToken.Trim().Length < 24)
        {
            throw new InvalidOperationException("Enter the import token configured in the web app.");
        }
    }

    private static string TrimResponse(string value)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 180 ? normalized : $"{normalized[..180]}…";
    }
}

internal sealed class PendingResultReview
{
    public string CaptureId { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
    public string OcrText { get; init; } = string.Empty;
    public string ReviewReason { get; init; } = string.Empty;
    public string ScreenshotPath { get; init; } = string.Empty;
}

internal sealed class ResultReviewUploadReceipt
{
    public bool Duplicate { get; set; }
    public string ReviewId { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
}
