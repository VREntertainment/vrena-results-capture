using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

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

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        AddText(
            archive,
            "manifest.txt",
            "VRena Results Capture support bundle\r\n" +
            $"Created: {timestamp:O}\r\n" +
            "ContainsImportToken: false\r\n" +
            "ContainsScreenshots: false\r\n" +
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

        DiagnosticLog.Info($"Support bundle created: {path}");
        return path;
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
