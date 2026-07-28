using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

namespace VRenaResultsCapture;

internal sealed class MonitorEngine : IDisposable
{
    private readonly CaptureSettings _settings;
    private readonly Bitmap _reference;
    private readonly System.Threading.Timer _timer;
    private int _callbackRunning;
    private int _consecutiveMatches;
    private int _consecutiveMisses;
    private bool _capturedCurrentAppearance;
    private bool _disposed;

    internal event Action<double, bool>? DetectionUpdated;
    internal event Action<string>? ScreenshotSaved;
    internal event Action<string>? CaptureError;

    internal bool IsRunning { get; private set; }

    internal MonitorEngine(CaptureSettings settings)
    {
        if (!settings.HasReference)
        {
            throw new InvalidOperationException("Configure screen recognition before monitoring.");
        }

        _settings = settings;
        _reference = new Bitmap(AppPaths.ReferenceImage);
        _timer = new System.Threading.Timer(CheckScreen, null, Timeout.Infinite, Timeout.Infinite);
    }

    internal void Start()
    {
        ThrowIfDisposed();
        _consecutiveMatches = 0;
        _consecutiveMisses = 0;
        _capturedCurrentAppearance = false;
        IsRunning = true;
        DiagnosticLog.Info("Monitor engine timer started.");
        _timer.Change(0, Math.Max(250, _settings.PollIntervalMilliseconds));
    }

    internal void Stop()
    {
        if (_disposed)
        {
            return;
        }

        IsRunning = false;
        DiagnosticLog.Info("Monitor engine timer stopped.");
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    internal string CaptureNow()
    {
        ThrowIfDisposed();
        return CaptureFullScreen(GetConfiguredScreen());
    }

    private void CheckScreen(object? state)
    {
        if (!IsRunning || Interlocked.Exchange(ref _callbackRunning, 1) == 1)
        {
            return;
        }

        try
        {
            var screen = GetConfiguredScreen();
            var area = _settings.DetectionArea?.ToRectangle()
                ?? throw new InvalidOperationException("The recognition area is not configured.");

            using var current = ScreenshotHelper.CaptureArea(screen, area);
            var similarity = ImageMatcher.Compare(_reference, current);
            var matched = similarity >= _settings.SimilarityThreshold;

            if (matched)
            {
                _consecutiveMatches++;
                _consecutiveMisses = 0;

                if (!_capturedCurrentAppearance &&
                    _consecutiveMatches >= Math.Max(1, _settings.ConsecutiveMatchesRequired))
                {
                    var path = CaptureFullScreen(screen);
                    _capturedCurrentAppearance = true;
                    DiagnosticLog.Info(
                        $"Results screen detected and captured. Similarity={similarity:F4}; File={path}");
                    ScreenshotSaved?.Invoke(path);
                }
            }
            else
            {
                _consecutiveMatches = 0;
                _consecutiveMisses++;

                if (_capturedCurrentAppearance &&
                    _consecutiveMisses >= Math.Max(2, _settings.ConsecutiveMissesToRearm))
                {
                    _capturedCurrentAppearance = false;
                }
            }

            DetectionUpdated?.Invoke(similarity, _capturedCurrentAppearance);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("Monitor engine screen check failed.", exception);
            CaptureError?.Invoke(exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _callbackRunning, 0);
        }
    }

    private string CaptureFullScreen(Screen screen)
    {
        if (AppPaths.IsInsideInstallDirectory(_settings.CaptureDirectory))
        {
            throw new InvalidOperationException(
                "Choose a capture folder outside the application installation folder.");
        }

        var timestamp = DateTimeOffset.Now;
        var monthlyDirectory = Path.Combine(
            _settings.CaptureDirectory,
            timestamp.ToString("yyyy", CultureInfo.InvariantCulture),
            timestamp.ToString("MM", CultureInfo.InvariantCulture));

        Directory.CreateDirectory(monthlyDirectory);

        var baseName = $"VRena_Result_{timestamp:yyyy-MM-dd_HH-mm-ss-fff}";
        var screenshotPath = Path.Combine(monthlyDirectory, $"{baseName}.png");
        var suffix = 1;
        while (File.Exists(screenshotPath))
        {
            screenshotPath = Path.Combine(monthlyDirectory, $"{baseName}_{suffix++}.png");
        }

        using var screenshot = ScreenshotHelper.CaptureScreen(screen);
        screenshot.Save(screenshotPath, ImageFormat.Png);
        AppendAuditLog(timestamp, screenshotPath, screen.DeviceName);
        return screenshotPath;
    }

    private void AppendAuditLog(DateTimeOffset timestamp, string screenshotPath, string monitorName)
    {
        Directory.CreateDirectory(_settings.CaptureDirectory);
        var logPath = Path.Combine(_settings.CaptureDirectory, "capture-log.csv");
        var needsHeader = !File.Exists(logPath);
        var relativePath = Path.GetRelativePath(_settings.CaptureDirectory, screenshotPath);
        var builder = new StringBuilder();

        if (needsHeader)
        {
            builder.AppendLine("captured_at_local,utc_offset,monitor,file");
        }

        builder
            .Append(Csv(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))).Append(',')
            .Append(Csv(timestamp.ToString("zzz", CultureInfo.InvariantCulture))).Append(',')
            .Append(Csv(monitorName)).Append(',')
            .AppendLine(Csv(relativePath));

        File.AppendAllText(logPath, builder.ToString(), new UTF8Encoding(false));
    }

    private Screen GetConfiguredScreen()
    {
        return Screen.AllScreens.FirstOrDefault(
                   screen => screen.DeviceName.Equals(
                       _settings.MonitorDeviceName,
                       StringComparison.OrdinalIgnoreCase))
               ?? Screen.PrimaryScreen
               ?? Screen.AllScreens.First();
    }

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        _reference.Dispose();
    }
}
