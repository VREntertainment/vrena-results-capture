using System.Diagnostics;

namespace VRenaResultsCapture;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Any(argument => argument.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Installer.Uninstall();
            return;
        }

        var portable = args.Any(argument => argument.Equals("--portable", StringComparison.OrdinalIgnoreCase));
        if (!portable && !Installer.IsRunningFromInstallFolder())
        {
            Installer.InstallAndLaunch();
            return;
        }

        using var mutex = new Mutex(true, @"Local\VRenaResultsCapture", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "VRena Results Capture is already running in the notification area.",
                "VRena Results Capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var settings = SettingsStore.Load();
        DiagnosticLog.Initialize(settings.CaptureDirectory);
        Application.ThreadException += (_, eventArgs) =>
            DiagnosticLog.Error("Unhandled Windows UI exception.", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                DiagnosticLog.Error("Unhandled application exception.", exception);
            }
            else
            {
                DiagnosticLog.Warning($"Unhandled non-exception object: {eventArgs.ExceptionObject}");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            DiagnosticLog.Error("Unobserved background task exception.", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        DiagnosticLog.Info("Starting main window.");
        Application.Run(new MainForm(settings));
        DiagnosticLog.Info("Application stopped.");
        GC.KeepAlive(mutex);
    }
}
