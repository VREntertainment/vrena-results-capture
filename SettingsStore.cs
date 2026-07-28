using System.Text.Json;

namespace VRenaResultsCapture;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal static CaptureSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return new CaptureSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsFile);
            var settings = JsonSerializer.Deserialize<CaptureSettings>(json, JsonOptions) ?? new CaptureSettings();
            if (AppPaths.IsInsideInstallDirectory(settings.CaptureDirectory))
            {
                settings.CaptureDirectory = AppPaths.DefaultCaptureDirectory;
            }

            return settings;
        }
        catch
        {
            return new CaptureSettings();
        }
    }

    internal static void Save(CaptureSettings settings)
    {
        Directory.CreateDirectory(AppPaths.InstallDirectory);
        var temporaryFile = $"{AppPaths.SettingsFile}.tmp";
        File.WriteAllText(temporaryFile, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryFile, AppPaths.SettingsFile, true);
    }
}
