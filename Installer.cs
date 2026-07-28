using System.Diagnostics;
using Microsoft.Win32;

namespace VRenaResultsCapture;

internal static class Installer
{
    internal static bool IsRunningFromInstallFolder()
    {
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable))
        {
            return false;
        }

        return Path.GetFullPath(currentExecutable)
            .Equals(Path.GetFullPath(AppPaths.InstalledExecutable), StringComparison.OrdinalIgnoreCase);
    }

    internal static void InstallAndLaunch()
    {
        var response = MessageBox.Show(
            "Install VRena Results Capture for this Windows user?\n\n" +
            "The application will start with Windows and save screenshots only on this computer. " +
            "It never deletes old captures.",
            "Install VRena Results Capture",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (response != DialogResult.OK)
        {
            return;
        }

        try
        {
            var sourceExecutable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The setup executable path is unavailable.");

            Directory.CreateDirectory(AppPaths.InstallDirectory);
            File.Copy(sourceExecutable, AppPaths.InstalledExecutable, true);

            SetRunAtLogin(true);
            CreateStartMenuShortcut();
            RegisterUninstaller();

            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.InstalledExecutable,
                Arguments = "--installed",
                UseShellExecute = true
            });

            MessageBox.Show(
                "Installation is complete.\n\n" +
                "VRena Results Capture has been added to the Start menu and will start automatically with Windows.",
                "VRena Results Capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Installation could not be completed:\n\n{exception.Message}",
                "VRena Results Capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    internal static void SetRunAtLogin(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            true);

        if (enabled)
        {
            runKey?.SetValue(
                AppPaths.RunRegistryName,
                $"\"{AppPaths.InstalledExecutable}\" --installed",
                RegistryValueKind.String);
        }
        else
        {
            runKey?.DeleteValue(AppPaths.RunRegistryName, false);
        }
    }

    internal static void Uninstall()
    {
        var response = MessageBox.Show(
            "Remove VRena Results Capture from this computer?\n\n" +
            "Saved screenshots and the capture log will remain untouched.",
            "Uninstall VRena Results Capture",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (response != DialogResult.OK)
        {
            return;
        }

        try
        {
            SetRunAtLogin(false);
            File.Delete(AppPaths.StartMenuShortcut);
            Registry.CurrentUser.DeleteSubKeyTree(AppPaths.UninstallRegistryKey, false);

            var escapedDirectory = AppPaths.InstallDirectory.Replace("'", "''", StringComparison.Ordinal);
            var command =
                $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
                $"Remove-Item -LiteralPath '{escapedDirectory}' -Recurse -Force";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "-NoProfile",
                    "-NonInteractive",
                    "-WindowStyle",
                    "Hidden",
                    "-Command",
                    command
                }
            });

            MessageBox.Show(
                "VRena Results Capture has been removed. Your screenshots were not deleted.",
                "VRena Results Capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Uninstall could not be completed:\n\n{exception.Message}",
                "VRena Results Capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void RegisterUninstaller()
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppPaths.UninstallRegistryKey, true);
        key?.SetValue("DisplayName", AppPaths.ProductName);
        key?.SetValue("DisplayVersion", "2.0.1");
        key?.SetValue("Publisher", "VRena");
        key?.SetValue("InstallLocation", AppPaths.InstallDirectory);
        key?.SetValue("DisplayIcon", AppPaths.InstalledExecutable);
        key?.SetValue("UninstallString", $"\"{AppPaths.InstalledExecutable}\" --uninstall");
        key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void CreateStartMenuShortcut()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.StartMenuShortcut)!);

        var target = AppPaths.InstalledExecutable.Replace("'", "''", StringComparison.Ordinal);
        var shortcut = AppPaths.StartMenuShortcut.Replace("'", "''", StringComparison.Ordinal);
        var workingDirectory = AppPaths.InstallDirectory.Replace("'", "''", StringComparison.Ordinal);
        var script =
            "$shell = New-Object -ComObject WScript.Shell; " +
            $"$link = $shell.CreateShortcut('{shortcut}'); " +
            $"$link.TargetPath = '{target}'; " +
            $"$link.WorkingDirectory = '{workingDirectory}'; " +
            "$link.Description = 'Automatically capture VRena results screens'; " +
            "$link.Save();";

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                script
            }
        });

        if (process is null ||
            !process.WaitForExit(10_000) ||
            process.ExitCode != 0 ||
            !File.Exists(AppPaths.StartMenuShortcut))
        {
            throw new InvalidOperationException("The Start menu shortcut could not be created.");
        }
    }
}
