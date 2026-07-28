using System.Text.Json.Serialization;

namespace VRenaResultsCapture;

internal sealed class CaptureSettings
{
    public string CaptureDirectory { get; set; } = AppPaths.DefaultCaptureDirectory;
    public string? MonitorDeviceName { get; set; }
    public DetectionRectangle? DetectionArea { get; set; }
    public double SimilarityThreshold { get; set; } = 0.82;
    public int PollIntervalMilliseconds { get; set; } = 750;
    public int ConsecutiveMatchesRequired { get; set; } = 2;
    public int ConsecutiveMissesToRearm { get; set; } = 4;
    public bool RunAtLogin { get; set; } = true;
    public bool StartMonitoringAutomatically { get; set; } = true;
    public bool SyncEnabled { get; set; }
    public string WebAppBaseUrl { get; set; } = "https://vrena-booking.vercel.app";
    public string IngestToken { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasReference =>
        DetectionArea is { Width: > 8, Height: > 8 } && File.Exists(AppPaths.ReferenceImage);
}

internal sealed class DetectionRectangle
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public Rectangle ToRectangle() => new(X, Y, Width, Height);

    public static DetectionRectangle FromRectangle(Rectangle rectangle) =>
        new()
        {
            X = rectangle.X,
            Y = rectangle.Y,
            Width = rectangle.Width,
            Height = rectangle.Height
        };
}
