using System.Globalization;
using System.Text;

namespace VRenaResultsCapture;

internal static class DiagnosticLog
{
    private static readonly object Gate = new();
    private static string? _captureDirectory;

    internal static void Initialize(string captureDirectory)
    {
        lock (Gate)
        {
            _captureDirectory = captureDirectory;
            Directory.CreateDirectory(LogDirectory());
            Directory.CreateDirectory(OcrDirectory());
        }

        Info(
            $"Application initialized. Version={Application.ProductVersion}; " +
            $"OS={Environment.OSVersion}; Runtime={Environment.Version}; " +
            $"ProcessArchitecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; " +
            $"Machine={Environment.MachineName}");
    }

    internal static void Info(string message) => Write("INFO", message);

    internal static void Warning(string message) => Write("WARN", message);

    internal static void Error(string context, Exception exception) =>
        Write("ERROR", $"{context}{Environment.NewLine}{exception}");

    internal static void SaveOcrText(string captureId, string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(OcrDirectory());
                var path = Path.Combine(OcrDirectory(), $"{captureId}.txt");
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, text, new UTF8Encoding(false));
                }
            }
        }
        catch (Exception exception)
        {
            Warning($"Could not save OCR diagnostic text: {exception.Message}");
        }
    }

    internal static string CurrentDiagnosticsDirectory()
    {
        lock (Gate)
        {
            return AppPaths.DiagnosticsDirectory(
                _captureDirectory ?? AppPaths.DefaultCaptureDirectory);
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory());
                var timestamp = DateTimeOffset.Now;
                var path = Path.Combine(
                    LogDirectory(),
                    $"{timestamp:yyyy-MM-dd}.log");
                var normalized = message.ReplaceLineEndings(Environment.NewLine);
                File.AppendAllText(
                    path,
                    $"{timestamp.ToString("O", CultureInfo.InvariantCulture)} " +
                    $"[{level}] [thread:{Environment.CurrentManagedThreadId}] {normalized}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never interrupt capture or synchronization.
        }
    }

    private static string LogDirectory() =>
        Path.Combine(CurrentDiagnosticsRoot(), "logs");

    private static string OcrDirectory() =>
        Path.Combine(CurrentDiagnosticsRoot(), "ocr");

    private static string CurrentDiagnosticsRoot() =>
        AppPaths.DiagnosticsDirectory(
            _captureDirectory ?? AppPaths.DefaultCaptureDirectory);
}
