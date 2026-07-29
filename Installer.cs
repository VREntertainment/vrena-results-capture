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
            "Install VRena Results Capture?",
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
                "Installation complete.",
                "VRena Results Capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception)
        {
            MessageBox.Show(
                "Couldn’t install the app. Please try again.",
                "VRena Results Capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    internal static void ApplyUpdate(string[] args)
    {
        try
        {
            var waitPid = ParseWaitProcessId(args);
            if (waitPid is not null)
            {
                try
                {
                    using var process = Process.GetProcessById(waitPid.Value);
                    if (!process.WaitForExit(60_000))
                    {
                        throw new InvalidOperationException("The previous version did not close in time.");
                    }
                }
                catch (ArgumentException)
                {
                    // The previous process already exited.
                }
            }

            var sourceExecutable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The update executable path is unavailable.");
            Directory.CreateDirectory(AppPaths.InstallDirectory);
            File.Copy(sourceExecutable, AppPaths.InstalledExecutable, true);
            SetRunAtLogin(true);
            CreateStartMenuShortcut();
            RegisterUninstaller();

            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.InstalledExecutable,
                Arguments = "--installed --updated",
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            MessageBox.Show(
                "Couldn’t install the update. Please try again.",
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
            "Remove VRena Results Capture?",
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
                "App removed. Screenshots were kept.",
                "VRena Results Capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception)
        {
            MessageBox.Show(
                "Couldn’t remove the app. Please try again.",
                "VRena Results Capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static int? ParseWaitProcessId(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals("--wait-pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out var processId) &&
                processId > 0)
            {
                return processId;
            }
        }

        return null;
    }

    private static void RegisterUninstaller()
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppPaths.UninstallRegistryKey, true);
        key?.SetValue("DisplayName", AppPaths.ProductName);
        key?.SetValue("DisplayVersion", Application.ProductVersion);
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
