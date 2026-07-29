using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VRenaResultsCapture;

internal static class SupportBundle
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal static string Create(CaptureSettings settings)
    {
        var bundleDirectory = AppPaths.SupportBundlesDirectory(settings.CaptureDirectory);
        Directory.CreateDirectory(bundleDirectory);
        var timestamp = DateTimeOffset.Now;
        var path = Path.Combine(
            bundleDirectory,
            $"VRena-Results-Capture-Support-{timestamp:yyyyMMdd-HHmmss}.zip");
        var latestScreenshot = FindLatestScreenshot(settings.CaptureDirectory);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        AddText(
            archive,
            "manifest.txt",
            "VRena Results Capture support bundle\r\n" +
            $"Created: {timestamp:O}\r\n" +
            "ContainsImportToken: false\r\n" +
            $"ContainsScreenshots: {latestScreenshot is not null} (latest capture only)\r\n" +
            "ContainsExecutable: false\r\n" +
            "MayContainPlayerNames: true\r\n" +
            "MayContainMachineAndLocalPathInformation: true\r\n" +
            "Treat this bundle as private support material.\r\n");

        AddText(
            archive,
            "system-info.txt",
            $"ApplicationVersion: {Application.ProductVersion}\r\n" +
            $"AssemblyVersion: {Assembly.GetExecutingAssembly().GetName().Version}\r\n" +
            $"CreatedAt: {timestamp:O}\r\n" +
            $"OS: {Environment.OSVersion}\r\n" +
            $"Runtime: {Environment.Version}\r\n" +
            $"ProcessArchitecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\r\n" +
            $"MachineName: {Environment.MachineName}\r\n" +
            $"UserInteractive: {Environment.UserInteractive}\r\n" +
            $"Screens: {string.Join("; ", Screen.AllScreens.Select(screen => $"{screen.DeviceName} {screen.Bounds.Width}x{screen.Bounds.Height} primary={screen.Primary}"))}\r\n");

        var sanitizedSettings = new
        {
            settings.CaptureDirectory,
            settings.MonitorDeviceName,
            settings.DetectionArea,
            settings.SimilarityThreshold,
            settings.PollIntervalMilliseconds,
            settings.ConsecutiveMatchesRequired,
            settings.ConsecutiveMissesToRearm,
            settings.RunAtLogin,
            settings.StartMonitoringAutomatically,
            settings.SyncEnabled,
            settings.WebAppBaseUrl,
            IngestTokenConfigured = !string.IsNullOrWhiteSpace(settings.IngestToken),
            IngestTokenLength = settings.IngestToken?.Length ?? 0
        };
        AddText(
            archive,
            "settings-sanitized.json",
            JsonSerializer.Serialize(sanitizedSettings, JsonOptions));

        AddRecentFiles(
            archive,
            Path.Combine(DiagnosticLog.CurrentDiagnosticsDirectory(), "logs"),
            "logs",
            "*.log",
            14);
        AddRecentFiles(
            archive,
            Path.Combine(DiagnosticLog.CurrentDiagnosticsDirectory(), "ocr"),
            "ocr",
            "*.txt",
            30);

        AddFileIfPresent(
            archive,
            Path.Combine(settings.CaptureDirectory, "capture-log.csv"),
            "history/capture-log.csv");
        AddFileIfPresent(
            archive,
            Path.Combine(settings.CaptureDirectory, "recognized-results.csv"),
            "history/recognized-results.csv");
        AddFileIfPresent(
            archive,
            AppPaths.ReferenceImage,
            "configuration/reference.png");
        if (latestScreenshot is not null)
        {
            AddCompressedScreenshot(archive, latestScreenshot, "latest-capture.jpg");
        }

        DiagnosticLog.Info($"Support bundle created: {path}");
        return path;
    }

    private static string? FindLatestScreenshot(string captureDirectory)
    {
        if (!Directory.Exists(captureDirectory))
        {
            return null;
        }

        var excludedDirectories = new[]
        {
            Path.GetFullPath(AppPaths.DiagnosticsDirectory(captureDirectory)),
            Path.GetFullPath(AppPaths.SupportBundlesDirectory(captureDirectory))
        };
        return Directory
            .EnumerateFiles(captureDirectory, "*.png", SearchOption.AllDirectories)
            .Where(file =>
            {
                var fullPath = Path.GetFullPath(file);
                return excludedDirectories.All(directory =>
                    !fullPath.StartsWith(
                        directory + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void AddCompressedScreenshot(
        ZipArchive archive,
        string sourcePath,
        string targetPath)
    {
        using var source = new Bitmap(sourcePath);
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
        quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 72L);
        var entry = archive.CreateEntry(targetPath, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        resized.Save(stream, encoder, quality);
    }

    private static void AddRecentFiles(
        ZipArchive archive,
        string directory,
        string targetDirectory,
        string searchPattern,
        int maximum)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory
                     .EnumerateFiles(directory, searchPattern)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(maximum))
        {
            archive.CreateEntryFromFile(
                file,
                $"{targetDirectory}/{Path.GetFileName(file)}",
                CompressionLevel.Optimal);
        }
    }

    private static void AddFileIfPresent(
        ZipArchive archive,
        string sourcePath,
        string targetPath)
    {
        if (File.Exists(sourcePath))
        {
            archive.CreateEntryFromFile(sourcePath, targetPath, CompressionLevel.Optimal);
        }
    }

    private static void AddText(
        ZipArchive archive,
        string targetPath,
        string content)
    {
        var entry = archive.CreateEntry(targetPath, CompressionLevel.Optimal);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(false));
        writer.Write(content);
    }
}
