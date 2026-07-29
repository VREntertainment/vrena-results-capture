namespace VRenaResultsCapture;

internal static class AppPaths
{
    internal const string ProductName = "VRena Results Capture";
    internal const string ExecutableName = "VRenaResultsCapture.exe";
    internal const string RunRegistryName = "VRenaResultsCapture";
    internal const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VRenaResultsCapture";

    internal static string InstallDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductName);

    internal static string InstalledExecutable =>
        Path.Combine(InstallDirectory, ExecutableName);

    internal static string SettingsFile =>
        Path.Combine(InstallDirectory, "settings.json");

    internal static string SettingsBackupFile =>
        Path.Combine(InstallDirectory, "settings.backup.json");

    internal static string ReferenceImage =>
        Path.Combine(InstallDirectory, "reference.png");

    internal static string DiagnosticsDirectory(string captureDirectory) =>
        Path.Combine(captureDirectory, "Diagnostics");

    internal static string SupportBundlesDirectory(string captureDirectory) =>
        Path.Combine(captureDirectory, "SupportBundles");

    internal static string StartMenuShortcut =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            $"{ProductName}.lnk");

    internal static string DefaultCaptureDirectory
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures))
            {
                pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(pictures, "VRena Results");
        }
    }

    internal static bool IsInsideInstallDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var candidate = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var installDirectory = Path.GetFullPath(InstallDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return candidate.StartsWith(installDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
