using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;

namespace VRenaResultsCapture;

internal static class SupportBundleUploadClient
{
    private const long MaximumBundleBytes = 3_500_000;
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    internal static async Task<SupportBundleUploadResult> UploadAsync(
        CaptureSettings settings,
        string bundlePath)
    {
        ValidateSettings(settings);
        var fileInfo = new FileInfo(bundlePath);
        if (!fileInfo.Exists || fileInfo.Length < 1)
        {
            throw new InvalidOperationException("The support bundle file is missing.");
        }
        if (fileInfo.Length > MaximumBundleBytes)
        {
            throw new InvalidOperationException(
                $"The support bundle is too large to upload ({fileInfo.Length / 1_000_000d:0.0} MB).");
        }

        var endpoint = $"{settings.WebAppBaseUrl.Trim().TrimEnd('/')}/api/venue/support-bundles";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.IngestToken.Trim());
        request.Headers.Add("X-VRena-Device-Name", Environment.MachineName);
        request.Headers.Add(
            "X-VRena-App-Version",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Application.ProductVersion);

        await using var stream = File.OpenRead(bundlePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var form = new MultipartFormDataContent();
        form.Add(fileContent, "bundle", fileInfo.Name);
        request.Content = form;

        using var response = await Client.SendAsync(request);
        DiagnosticLog.Info($"Support bundle upload returned HTTP {(int)response.StatusCode}.");
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Support bundle upload failed ({(int)response.StatusCode}). {TrimResponse(detail)}");
        }

        return await response.Content.ReadFromJsonAsync<SupportBundleUploadResult>()
            ?? throw new InvalidOperationException("The support bundle upload receipt is invalid.");
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

internal sealed class SupportBundleUploadResult
{
    public string BundleId { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
}
