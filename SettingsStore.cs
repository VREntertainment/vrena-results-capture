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
        var primary = TryLoad(AppPaths.SettingsFile);
        var backup = TryLoad(AppPaths.SettingsBackupFile);
        var settings = primary ?? backup ?? new CaptureSettings();
        var shouldPersistSettings = primary is null && backup is not null;
        settings.WebAppBaseUrl ??= string.Empty;
        settings.IngestToken ??= string.Empty;

        if (!HasValidWebAppUrl(settings.WebAppBaseUrl) &&
            backup is not null &&
            HasValidWebAppUrl(backup.WebAppBaseUrl))
        {
            settings.WebAppBaseUrl = backup.WebAppBaseUrl ?? string.Empty;
        }

        if (IsLegacyWebAppUrl(settings.WebAppBaseUrl))
        {
            settings.WebAppBaseUrl = CaptureSettings.CanonicalWebAppBaseUrl;
            shouldPersistSettings = true;
        }

        if (settings.IngestToken.Trim().Length < 24 &&
            backup is not null &&
            (backup.IngestToken?.Trim().Length ?? 0) >= 24)
        {
            settings.IngestToken = backup.IngestToken ?? string.Empty;
        }

        if (AppPaths.IsInsideInstallDirectory(settings.CaptureDirectory))
        {
            settings.CaptureDirectory = AppPaths.DefaultCaptureDirectory;
        }

        if (shouldPersistSettings)
        {
            Save(settings);
        }

        return settings;
    }

    internal static void Save(CaptureSettings settings)
    {
        Directory.CreateDirectory(AppPaths.InstallDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        WriteAtomic(AppPaths.SettingsFile, json);

        if (HasValidWebAppUrl(settings.WebAppBaseUrl) &&
            settings.IngestToken.Trim().Length >= 24)
        {
            WriteAtomic(AppPaths.SettingsBackupFile, json);
        }
    }

    private static CaptureSettings? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CaptureSettings>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteAtomic(string path, string json)
    {
        var temporaryFile = $"{path}.tmp";
        try
        {
            File.WriteAllText(temporaryFile, json);
            File.Move(temporaryFile, path, true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    private static bool HasValidWebAppUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsLegacyWebAppUrl(string? value) =>
        string.Equals(
            value?.Trim().TrimEnd('/'),
            "https://vrena-booking.vercel.app",
            StringComparison.OrdinalIgnoreCase);
}
