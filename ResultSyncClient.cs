using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace VRenaResultsCapture;

internal static class ResultSyncClient
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static async Task<ResultProcessingOutcome> ProcessAsync(
        CaptureSettings settings,
        string screenshotPath,
        DateTimeOffset capturedAt)
    {
        var readOutcome = await WindowsResultReader.ReadAsync(screenshotPath, capturedAt);
        var result = readOutcome.Result;
        if (result is null)
        {
            if (!settings.SyncEnabled)
            {
                return new ResultProcessingOutcome(
                    "Screenshot saved. Review upload is off.",
                    false,
                    false);
            }

            ValidateSyncSettings(settings);
            QueuePendingReview(
                settings.CaptureDirectory,
                new PendingResultReview
                {
                    CaptureId = readOutcome.CaptureId,
                    CapturedAt = capturedAt,
                    OcrText = readOutcome.OcrText,
                    ReviewReason = readOutcome.ReviewReason ?? "players_not_recognized",
                    ScreenshotPath = screenshotPath
                });
            var uploadedCount = await RetryPendingReviewsAsync(settings);
            DiagnosticLog.Info(
                $"Review synchronization completed. CaptureId={readOutcome.CaptureId}; PendingSent={uploadedCount}");
            return new ResultProcessingOutcome(
                "Screenshot uploaded for review.",
                false,
                true);
        }

        AppendLocalResultHistory(settings.CaptureDirectory, result, screenshotPath);
        DiagnosticLog.Info(
            $"Recognized result history saved. CaptureId={result.CaptureId}; " +
            $"Game={result.GameSlug}; Players={result.Players.Count}");

        if (!settings.SyncEnabled)
        {
            return new ResultProcessingOutcome(
                "Result saved. Sync is off.",
                true,
                false);
        }

        ValidateSyncSettings(settings);
        QueuePendingResult(settings.CaptureDirectory, result);
        var syncedCount = await RetryPendingAsync(settings);
        DiagnosticLog.Info(
            $"Web synchronization completed. CaptureId={result.CaptureId}; PendingSent={syncedCount}");

        return new ResultProcessingOutcome(
            "Result saved and synced.",
            true,
            true);
    }

    internal static async Task TestConnectionAsync(CaptureSettings settings)
    {
        ValidateSyncSettings(settings);
        using var request = CreateRequest(settings, HttpMethod.Get);
        using var response = await Client.SendAsync(request);
        DiagnosticLog.Info($"Web sync connection test returned HTTP {(int)response.StatusCode}.");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Connection failed ({(int)response.StatusCode}). Check the URL and import token.");
        }
    }

    internal static async Task<int> RetryPendingAsync(CaptureSettings settings)
    {
        if (!settings.SyncEnabled)
        {
            return 0;
        }

        ValidateSyncSettings(settings);
        var pendingDirectory = PendingDirectory(settings.CaptureDirectory);
        if (!Directory.Exists(pendingDirectory))
        {
            return 0;
        }

        var syncedCount = 0;
        foreach (var pendingPath in Directory.EnumerateFiles(pendingDirectory, "*.json").Order())
        {
            var result = JsonSerializer.Deserialize<RecognizedResult>(
                await File.ReadAllTextAsync(pendingPath),
                JsonOptions);
            if (result is null)
            {
                continue;
            }

            await SendResultAsync(settings, result);
            WriteSyncReceipt(settings.CaptureDirectory, result);
            File.Delete(pendingPath);
            syncedCount++;
        }

        return syncedCount;
    }

    internal static async Task<int> RetryPendingReviewsAsync(CaptureSettings settings)
    {
        if (!settings.SyncEnabled)
        {
            return 0;
        }

        ValidateSyncSettings(settings);
        var pendingDirectory = PendingReviewDirectory(settings.CaptureDirectory);
        if (!Directory.Exists(pendingDirectory))
        {
            return 0;
        }

        var uploadedCount = 0;
        foreach (var pendingPath in Directory.EnumerateFiles(pendingDirectory, "*.json").Order())
        {
            var review = JsonSerializer.Deserialize<PendingResultReview>(
                await File.ReadAllTextAsync(pendingPath),
                JsonOptions);
            if (review is null)
            {
                continue;
            }

            var receipt = await ResultReviewUploadClient.UploadAsync(settings, review);
            WriteReviewReceipt(settings.CaptureDirectory, review, receipt);
            File.Delete(pendingPath);
            uploadedCount++;
        }

        return uploadedCount;
    }

    private static HttpRequestMessage CreateRequest(CaptureSettings settings, HttpMethod method)
    {
        var endpoint = $"{settings.WebAppBaseUrl.Trim().TrimEnd('/')}/api/venue/results";
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.IngestToken.Trim());
        return request;
    }

    private static async Task SendResultAsync(CaptureSettings settings, RecognizedResult result)
    {
        using var request = CreateRequest(settings, HttpMethod.Post);
        request.Content = JsonContent.Create(result, options: JsonOptions);
        using var response = await Client.SendAsync(request);
        DiagnosticLog.Info(
            $"Web result import returned HTTP {(int)response.StatusCode}. CaptureId={result.CaptureId}");
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Web sync failed ({(int)response.StatusCode}). {TrimResponse(detail)}");
        }
    }

    private static void ValidateSyncSettings(CaptureSettings settings)
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

    private static void AppendLocalResultHistory(
        string captureDirectory,
        RecognizedResult result,
        string screenshotPath)
    {
        var directory = Path.Combine(captureDirectory, "recognized-results");
        Directory.CreateDirectory(directory);
        var jsonPath = Path.Combine(directory, $"{result.CaptureId}.json");
        if (!File.Exists(jsonPath))
        {
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));
        }

        var csvPath = Path.Combine(captureDirectory, "recognized-results.csv");
        var needsHeader = !File.Exists(csvPath);
        var rows = new StringBuilder();
        if (needsHeader)
        {
            rows.AppendLine("captured_at,session,game,player,hits,accuracy_percent,movement_meters,score,screenshot");
        }

        foreach (var player in result.Players)
        {
            rows
                .Append(Csv(result.CapturedAt.ToString("O"))).Append(',')
                .Append(Csv(result.ExternalSessionLabel ?? string.Empty)).Append(',')
                .Append(Csv(result.GameName)).Append(',')
                .Append(Csv(player.Name)).Append(',')
                .Append(player.Hits).Append(',')
                .Append(player.AccuracyPercent?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(player.MovementMeters?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(player.Score).Append(',')
                .AppendLine(Csv(Path.GetRelativePath(captureDirectory, screenshotPath)));
        }

        File.AppendAllText(csvPath, rows.ToString(), new UTF8Encoding(false));
    }

    private static void QueuePendingResult(string captureDirectory, RecognizedResult result)
    {
        var directory = PendingDirectory(captureDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{result.CaptureId}.json");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));
        }
    }

    private static string PendingDirectory(string captureDirectory) =>
        Path.Combine(captureDirectory, "sync-pending");

    private static void QueuePendingReview(string captureDirectory, PendingResultReview review)
    {
        var directory = PendingReviewDirectory(captureDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{review.CaptureId}.json");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, JsonSerializer.Serialize(review, JsonOptions), new UTF8Encoding(false));
        }
    }

    private static string PendingReviewDirectory(string captureDirectory) =>
        Path.Combine(captureDirectory, "review-pending");

    private static void WriteSyncReceipt(string captureDirectory, RecognizedResult result)
    {
        var directory = Path.Combine(captureDirectory, "sync-receipts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{result.CaptureId}.json");
        if (!File.Exists(path))
        {
            var receipt = new
            {
                result.CaptureId,
                SyncedAt = DateTimeOffset.Now
            };
            File.WriteAllText(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false));
        }
    }

    private static void WriteReviewReceipt(
        string captureDirectory,
        PendingResultReview review,
        ResultReviewUploadReceipt receipt)
    {
        var directory = Path.Combine(captureDirectory, "review-receipts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{review.CaptureId}.json");
        if (!File.Exists(path))
        {
            var savedReceipt = new
            {
                review.CaptureId,
                receipt.Duplicate,
                receipt.ReviewId,
                receipt.UploadedAt
            };
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(savedReceipt, JsonOptions),
                new UTF8Encoding(false));
        }
    }

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string TrimResponse(string value)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 180 ? normalized : $"{normalized[..180]}…";
    }
}
