using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace VRenaResultsCapture;

internal static class UpdateService
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    internal static async Task<UpdateCheckResult> CheckAsync(CaptureSettings settings)
    {
        ValidateConnectionSettings(settings);
        var endpoint = $"{settings.WebAppBaseUrl.Trim().TrimEnd('/')}/api/venue/windows/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.IngestToken.Trim());
        using var response = await Client.SendAsync(request);
        DiagnosticLog.Info($"Update check returned HTTP {(int)response.StatusCode}.");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Update check failed ({(int)response.StatusCode}).");
        }

        var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>()
            ?? throw new InvalidOperationException("The update information is invalid.");
        var availableVersion = ParseVersion(manifest.Version, "available");
        var currentVersion = ParseVersion(Application.ProductVersion, "installed");
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps ||
            !downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The update download URL failed validation.");
        }
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("The update security fingerprint failed validation.");
        }

        DiagnosticLog.Info(
            $"Update information validated. Installed={currentVersion}; Available={availableVersion}");
        return new UpdateCheckResult(
            availableVersion > currentVersion,
            currentVersion,
            availableVersion,
            manifest);
    }

    internal static async Task DownloadAndInstallAsync(UpdateManifest manifest)
    {
        var updateDirectory = Path.Combine(AppPaths.InstallDirectory, "Updates", manifest.Version);
        Directory.CreateDirectory(updateDirectory);
        var downloadPath = Path.Combine(updateDirectory, "VRenaResultsCapture-Update.exe");
        var temporaryPath = $"{downloadPath}.download";

        using (var response = await Client.GetAsync(
                   manifest.DownloadUrl,
                   HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = File.Create(temporaryPath);
            await source.CopyToAsync(destination);
        }

        await using (var update = File.OpenRead(temporaryPath))
        {
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(update)).ToLowerInvariant();
            if (!digest.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPath);
                throw new InvalidOperationException("The downloaded update did not pass its security check.");
            }
        }

        File.Move(temporaryPath, downloadPath, true);
        DiagnosticLog.Info($"Verified update {manifest.Version}; launching installer.");
        Process.Start(new ProcessStartInfo
        {
            FileName = downloadPath,
            UseShellExecute = true,
            ArgumentList =
            {
                "--apply-update",
                "--wait-pid",
                Environment.ProcessId.ToString()
            }
        });
    }

    internal static Version ParseVersion(string value, string label)
    {
        var normalized = (value ?? string.Empty).Trim();
        var metadataIndex = normalized.IndexOfAny(['+', '-']);
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        if (Version.TryParse(normalized, out var version))
        {
            return version;
        }

        throw new InvalidOperationException(
            $"The {label} application version failed validation.");
    }

    private static void ValidateConnectionSettings(CaptureSettings settings)
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
}

internal sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    Version CurrentVersion,
    Version AvailableVersion,
    UpdateManifest Manifest);

internal sealed class UpdateManifest
{
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}
