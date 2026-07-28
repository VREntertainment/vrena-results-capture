namespace VRenaResultsCapture;

internal sealed class RecognizedResult
{
    public string CaptureId { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
    public string DeviceName { get; init; } = Environment.MachineName;
    public string? ExternalSessionLabel { get; init; }
    public string GameName { get; init; } = string.Empty;
    public string GameSlug { get; init; } = string.Empty;
    public List<RecognizedPlayer> Players { get; init; } = [];
}

internal sealed class RecognizedPlayer
{
    public string Name { get; init; } = string.Empty;
    public int Hits { get; init; }
    public double? AccuracyPercent { get; init; }
    public double? MovementMeters { get; init; }
    public int Score { get; init; }
}

internal sealed record ResultProcessingOutcome(
    string Message,
    bool Recognized,
    bool Synced);
