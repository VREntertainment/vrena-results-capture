using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VRenaResultsCapture;

internal sealed class MainForm : Form
{
    private const uint WindowDisplayAffinityExcludeFromCapture = 0x00000011;

    private readonly CaptureSettings _settings;
    private readonly ComboBox _monitorSelector = new();
    private readonly TextBox _captureDirectory = new();
    private readonly Label _referenceStatus = new();
    private readonly Label _monitoringStatus = new();
    private readonly Label _lastCapture = new();
    private readonly Label _syncStatus = new();
    private readonly Button _monitorButton = new();
    private readonly Button _captureNowButton = new();
    private readonly CheckBox _runAtLogin = new();
    private readonly CheckBox _syncEnabled = new();
    private readonly TextBox _webAppUrl = new();
    private readonly TextBox _ingestToken = new();
    private readonly Icon _applicationIcon = LoadApplicationIcon();
    private readonly NotifyIcon _trayIcon = new();
    private MonitorEngine? _engine;
    private bool _allowClose;
    private bool _shownCloseHint;

    internal MainForm(CaptureSettings? settings = null)
    {
        _settings = settings ?? SettingsStore.Load();
        DiagnosticLog.Initialize(_settings.CaptureDirectory);
        Text = AppPaths.ProductName;
        Icon = _applicationIcon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 760);
        Size = new Size(860, 820);
        BackColor = Color.FromArgb(246, 247, 251);
        Font = new Font("Segoe UI", 10);

        BuildInterface();
        ConfigureTrayIcon();
        LoadSettingsIntoControls();
        FormClosing += HandleFormClosing;
        Shown += HandleShown;
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 22),
            RowCount = 6,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 30, 42),
            Text = "VRena Results Capture",
            Margin = new Padding(0, 0, 0, 4)
        };
        var subtitle = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(86, 90, 108),
            Text = "Automatically saves one local screenshot whenever the configured results screen appears.",
            Margin = new Padding(0, 0, 0, 18)
        };
        var heading = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Fill
        };
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);
        root.Controls.Add(heading);

        root.Controls.Add(BuildConfigurationCard());
        root.Controls.Add(BuildWebSyncCard());
        root.Controls.Add(BuildStatusCard());
        root.Controls.Add(BuildActions());

        var note = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = Color.FromArgb(90, 94, 112),
            Text =
                "Screenshots stay local and old files are never deleted. When web sync is enabled, only the recognized " +
                "game, exact player name, date/time and result statistics are sent to the web app. " +
                "Closing this window keeps monitoring active in the notification area.",
            MaximumSize = new Size(730, 0),
            Margin = new Padding(2, 14, 2, 0)
        };
        root.Controls.Add(note);
    }

    private Control BuildWebSyncCard()
    {
        var card = CreateCard();
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(18)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        card.Controls.Add(layout);

        AddSectionTitle(layout, "Web app result sync", 0);

        layout.Controls.Add(CreateFieldLabel("Automatic sync"), 0, 1);
        _syncEnabled.AutoSize = true;
        _syncEnabled.Text = "Send recognized statistics after each local capture";
        _syncEnabled.CheckedChanged += (_, _) =>
        {
            _settings.SyncEnabled = _syncEnabled.Checked;
            SaveWebSyncSettings();
        };
        layout.Controls.Add(_syncEnabled, 1, 1);
        layout.SetColumnSpan(_syncEnabled, 2);

        layout.Controls.Add(CreateFieldLabel("Web app URL"), 0, 2);
        _webAppUrl.Dock = DockStyle.Fill;
        _webAppUrl.Leave += (_, _) => SaveWebSyncSettings();
        layout.Controls.Add(_webAppUrl, 1, 2);
        layout.SetColumnSpan(_webAppUrl, 2);

        layout.Controls.Add(CreateFieldLabel("Import token"), 0, 3);
        _ingestToken.Dock = DockStyle.Fill;
        _ingestToken.UseSystemPasswordChar = true;
        _ingestToken.Leave += (_, _) => SaveWebSyncSettings();
        layout.Controls.Add(_ingestToken, 1, 3);

        var testButton = new Button { Text = "Test", AutoSize = true };
        testButton.Click += async (_, _) => await TestWebConnectionAsync(testButton);
        layout.Controls.Add(testButton, 2, 3);

        layout.Controls.Add(CreateFieldLabel("Sync status"), 0, 4);
        _syncStatus.AutoSize = true;
        _syncStatus.Anchor = AnchorStyles.Left;
        _syncStatus.Text = "Not tested";
        layout.Controls.Add(_syncStatus, 1, 4);
        layout.SetColumnSpan(_syncStatus, 2);

        return card;
    }

    private Control BuildConfigurationCard()
    {
        var card = CreateCard();
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(18)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        card.Controls.Add(layout);

        AddSectionTitle(layout, "Capture configuration", 0);

        layout.Controls.Add(CreateFieldLabel("Display to watch"), 0, 1);
        _monitorSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _monitorSelector.Dock = DockStyle.Fill;
        _monitorSelector.SelectedIndexChanged += (_, _) => SaveControlsToSettings();
        layout.Controls.Add(_monitorSelector, 1, 1);
        layout.SetColumnSpan(_monitorSelector, 2);

        layout.Controls.Add(CreateFieldLabel("Local save folder"), 0, 2);
        _captureDirectory.Dock = DockStyle.Fill;
        _captureDirectory.ReadOnly = true;
        layout.Controls.Add(_captureDirectory, 1, 2);

        var browseButton = new Button { Text = "Choose…", AutoSize = true };
        browseButton.Click += (_, _) => ChooseCaptureDirectory();
        layout.Controls.Add(browseButton, 2, 2);

        layout.Controls.Add(CreateFieldLabel("Screen recognition"), 0, 3);
        _referenceStatus.AutoSize = true;
        _referenceStatus.Anchor = AnchorStyles.Left;
        layout.Controls.Add(_referenceStatus, 1, 3);

        var configureButton = new Button
        {
            Text = "Configure…",
            AutoSize = true,
            BackColor = Color.FromArgb(76, 48, 220),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        configureButton.FlatAppearance.BorderSize = 0;
        configureButton.Click += (_, _) => ConfigureRecognition();
        layout.Controls.Add(configureButton, 2, 3);

        return card;
    }

    private Control BuildStatusCard()
    {
        var card = CreateCard();
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(18),
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        AddSectionTitle(layout, "Status", 0);
        layout.Controls.Add(CreateFieldLabel("Monitoring"), 0, 1);
        _monitoringStatus.AutoSize = true;
        _monitoringStatus.Anchor = AnchorStyles.Left;
        layout.Controls.Add(_monitoringStatus, 1, 1);

        layout.Controls.Add(CreateFieldLabel("Last screenshot"), 0, 2);
        _lastCapture.AutoSize = true;
        _lastCapture.Anchor = AnchorStyles.Left;
        _lastCapture.Text = "None in this run";
        layout.Controls.Add(_lastCapture, 1, 2);

        layout.Controls.Add(CreateFieldLabel("Start behavior"), 0, 3);
        _runAtLogin.AutoSize = true;
        _runAtLogin.Text = "Start automatically when this Windows user signs in";
        _runAtLogin.CheckedChanged += (_, _) =>
        {
            _settings.RunAtLogin = _runAtLogin.Checked;
            SettingsStore.Save(_settings);
            Installer.SetRunAtLogin(_runAtLogin.Checked);
        };
        layout.Controls.Add(_runAtLogin, 1, 3);

        return card;
    }

    private Control BuildActions()
    {
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 16, 0, 0)
        };

        _monitorButton.Text = "Start monitoring";
        _monitorButton.AutoSize = true;
        _monitorButton.Padding = new Padding(10, 5, 10, 5);
        _monitorButton.BackColor = Color.FromArgb(76, 48, 220);
        _monitorButton.ForeColor = Color.White;
        _monitorButton.FlatStyle = FlatStyle.Flat;
        _monitorButton.FlatAppearance.BorderSize = 0;
        _monitorButton.Click += (_, _) => ToggleMonitoring();
        actions.Controls.Add(_monitorButton);

        _captureNowButton.Text = "Capture now";
        _captureNowButton.AutoSize = true;
        _captureNowButton.Padding = new Padding(8, 5, 8, 5);
        _captureNowButton.Click += (_, _) => CaptureNow();
        actions.Controls.Add(_captureNowButton);

        var openFolderButton = new Button
        {
            Text = "Open captures folder",
            AutoSize = true,
            Padding = new Padding(8, 5, 8, 5)
        };
        openFolderButton.Click += (_, _) => OpenCaptureDirectory();
        actions.Controls.Add(openFolderButton);

        var supportBundleButton = new Button
        {
            Text = "Create support bundle",
            AutoSize = true,
            Padding = new Padding(8, 5, 8, 5)
        };
        supportBundleButton.Click += (_, _) => CreateSupportBundle();
        actions.Controls.Add(supportBundleButton);

        var uploadSupportBundleButton = new Button
        {
            Text = "Upload support bundle",
            AutoSize = true,
            Padding = new Padding(8, 5, 8, 5)
        };
        uploadSupportBundleButton.Click += async (_, _) =>
            await UploadSupportBundleAsync(uploadSupportBundleButton);
        actions.Controls.Add(uploadSupportBundleButton);

        var updateButton = new Button
        {
            Text = "Check for updates",
            AutoSize = true,
            Padding = new Padding(8, 5, 8, 5)
        };
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync(false, updateButton);
        actions.Controls.Add(updateButton);

        return actions;
    }

    private static Panel CreateCard() =>
        new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 0, 14),
            BorderStyle = BorderStyle.FixedSingle
        };

    private static Label CreateFieldLabel(string text) =>
        new()
        {
            AutoSize = true,
            Text = text,
            ForeColor = Color.FromArgb(72, 76, 94),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 9, 12, 9)
        };

    private static void AddSectionTitle(TableLayoutPanel layout, string text, int row)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = text,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 38, 52),
            Margin = new Padding(0, 0, 0, 12)
        };
        layout.Controls.Add(label, 0, row);
        layout.SetColumnSpan(label, layout.ColumnCount);
    }

    private void LoadSettingsIntoControls()
    {
        _monitorSelector.Items.Clear();
        foreach (var screen in Screen.AllScreens)
        {
            _monitorSelector.Items.Add(new ScreenChoice(screen));
        }

        var selectedIndex = 0;
        for (var index = 0; index < _monitorSelector.Items.Count; index++)
        {
            if (_monitorSelector.Items[index] is ScreenChoice choice &&
                choice.Screen.DeviceName.Equals(_settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = index;
                break;
            }
        }

        if (_monitorSelector.Items.Count > 0)
        {
            _monitorSelector.SelectedIndex = selectedIndex;
        }

        _captureDirectory.Text = _settings.CaptureDirectory;
        _runAtLogin.Checked = _settings.RunAtLogin;
        _syncEnabled.Checked = _settings.SyncEnabled;
        _webAppUrl.Text = _settings.WebAppBaseUrl;
        _ingestToken.Text = _settings.IngestToken;
        UpdateReferenceStatus();
        UpdateMonitoringStatus("Stopped", Color.FromArgb(154, 71, 40));
    }

    private void HandleShown(object? sender, EventArgs eventArgs)
    {
        if (_settings.StartMonitoringAutomatically && _settings.HasReference)
        {
            StartMonitoring();
            WindowState = FormWindowState.Minimized;
            Hide();
        }

        if (_settings.IngestToken.Trim().Length >= 24)
        {
            _ = CheckForUpdatesAsync(true);
        }
    }

    private void ConfigureTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Capture now", null, (_, _) => CaptureNow());
        menu.Items.Add("Start / pause monitoring", null, (_, _) => ToggleMonitoring());
        menu.Items.Add("Open captures folder", null, (_, _) => OpenCaptureDirectory());
        menu.Items.Add("Upload support bundle", null, async (_, _) => await UploadSupportBundleAsync());
        menu.Items.Add("Check for updates", null, async (_, _) => await CheckForUpdatesAsync(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _allowClose = true;
            Close();
        });

        _trayIcon.Text = AppPaths.ProductName;
        _trayIcon.Icon = _applicationIcon;
        _trayIcon.Visible = true;
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose || eventArgs.CloseReason == CloseReason.WindowsShutDown)
        {
            _engine?.Dispose();
            _trayIcon.Visible = false;
            return;
        }

        eventArgs.Cancel = true;
        Hide();
        if (!_shownCloseHint)
        {
            _shownCloseHint = true;
            _trayIcon.ShowBalloonTip(
                3000,
                AppPaths.ProductName,
                "Monitoring continues here in the notification area.",
                ToolTipIcon.Info);
        }
    }

    private void ChooseCaptureDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the local folder where VRena results screenshots will be kept.",
            SelectedPath = _settings.CaptureDirectory,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (AppPaths.IsInsideInstallDirectory(dialog.SelectedPath))
        {
            MessageBox.Show(
                "Choose a different folder for screenshots.",
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _captureDirectory.Text = dialog.SelectedPath;
        _settings.CaptureDirectory = dialog.SelectedPath;
        SettingsStore.Save(_settings);
        DiagnosticLog.Initialize(_settings.CaptureDirectory);
        DiagnosticLog.Info("Capture and diagnostics directory changed.");
    }

    private void ConfigureRecognition()
    {
        var screen = GetSelectedScreen();
        if (screen is null)
        {
            MessageBox.Show(
                "No display is available.",
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var wasRunning = _engine?.IsRunning == true;
        StopMonitoring();
        Hide();
        Thread.Sleep(250);

        using var selector = new ReferenceSelectionForm(screen);
        var result = selector.ShowDialog();
        ShowFromTray();

        if (result == DialogResult.OK && selector.SelectedImage is not null)
        {
            Directory.CreateDirectory(AppPaths.InstallDirectory);
            selector.SelectedImage.Save(AppPaths.ReferenceImage, ImageFormat.Png);
            selector.SelectedImage.Dispose();

            _settings.MonitorDeviceName = screen.DeviceName;
            _settings.DetectionArea = DetectionRectangle.FromRectangle(selector.SelectedArea);
            SettingsStore.Save(_settings);
            DiagnosticLog.Info(
                $"Screen recognition configured. Monitor={screen.DeviceName}; " +
                $"Area={selector.SelectedArea.X},{selector.SelectedArea.Y}," +
                $"{selector.SelectedArea.Width}x{selector.SelectedArea.Height}");
            UpdateReferenceStatus();

            MessageBox.Show(
                "Screen recognition is ready.",
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            if (wasRunning || _settings.StartMonitoringAutomatically)
            {
                StartMonitoring();
            }
        }
        else if (wasRunning)
        {
            StartMonitoring();
        }
    }

    private void SaveControlsToSettings()
    {
        if (_monitorSelector.SelectedItem is not ScreenChoice choice)
        {
            return;
        }

        if (!string.Equals(
                _settings.MonitorDeviceName,
                choice.Screen.DeviceName,
                StringComparison.OrdinalIgnoreCase))
        {
            _settings.MonitorDeviceName = choice.Screen.DeviceName;
            if (_settings.DetectionArea is not null)
            {
                _settings.DetectionArea = null;
            }
        }

        SettingsStore.Save(_settings);
        UpdateReferenceStatus();
    }

    private void ToggleMonitoring()
    {
        if (_engine?.IsRunning == true)
        {
            StopMonitoring();
        }
        else
        {
            StartMonitoring();
        }
    }

    private void StartMonitoring()
    {
        if (!_settings.HasReference)
        {
            MessageBox.Show(
                "Show a results screen, then choose Configure.",
                "Configure screen recognition",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _engine?.Dispose();
        _engine = new MonitorEngine(_settings);
        _engine.DetectionUpdated += HandleDetectionUpdated;
        _engine.ScreenshotSaved += HandleScreenshotSaved;
        _engine.CaptureError += HandleCaptureError;
        _engine.Start();
        _ = RetryPendingResultsAsync();
        DiagnosticLog.Info(
            $"Monitoring started. Monitor={_settings.MonitorDeviceName}; " +
            $"ReferenceConfigured={_settings.HasReference}; SyncEnabled={_settings.SyncEnabled}; " +
            $"WebAppUrl={_settings.WebAppBaseUrl}; TokenConfigured={!string.IsNullOrWhiteSpace(_settings.IngestToken)}");
        _monitorButton.Text = "Pause monitoring";
        UpdateMonitoringStatus("Watching for results…", Color.FromArgb(29, 126, 77));
    }

    private void StopMonitoring()
    {
        if (_engine is not null)
        {
            _engine.Stop();
            _engine.Dispose();
            _engine = null;
        }

        _monitorButton.Text = "Start monitoring";
        UpdateMonitoringStatus("Stopped", Color.FromArgb(154, 71, 40));
        DiagnosticLog.Info("Monitoring stopped.");
    }

    private void CaptureNow()
    {
        try
        {
            SaveControlsToSettings();
            using var engine = new MonitorEngineForManualCapture(_settings, GetSelectedScreen());
            var path = engine.Capture();
            DiagnosticLog.Info($"Manual screenshot saved: {path}");
            HandleScreenshotSaved(path);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("Manual screenshot failed.", exception);
            MessageBox.Show(
                "Couldn’t save the screenshot. Please try again.",
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void HandleDetectionUpdated(double similarity, bool alreadyCaptured)
    {
        SafeUi(() =>
        {
            var percentage = similarity.ToString("P0");
            if (alreadyCaptured)
            {
                UpdateMonitoringStatus(
                    $"Result captured · waiting for screen to disappear · match {percentage}",
                    Color.FromArgb(29, 126, 77));
            }
            else
            {
                UpdateMonitoringStatus(
                    $"Watching for results… · current match {percentage}",
                    Color.FromArgb(29, 126, 77));
            }
        });
    }

    private void HandleScreenshotSaved(string path)
    {
        SafeUi(() =>
        {
            _lastCapture.Text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · {Path.GetFileName(path)}";
            _trayIcon.ShowBalloonTip(
                3500,
                "VRena result saved",
                path,
                ToolTipIcon.Info);
        });
        _ = ProcessSavedScreenshotAsync(path);
    }

    private async Task ProcessSavedScreenshotAsync(string path)
    {
        try
        {
            var capturedAt = new DateTimeOffset(File.GetCreationTime(path));
            var outcome = await ResultSyncClient.ProcessAsync(_settings, path, capturedAt);
            SafeUi(() =>
            {
                _syncStatus.Text = outcome.Message;
                _syncStatus.ForeColor = outcome.Synced
                    ? Color.FromArgb(29, 126, 77)
                    : outcome.Recognized
                        ? Color.FromArgb(128, 92, 25)
                        : Color.FromArgb(154, 71, 40);
            });
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error($"Result processing failed for {path}.", exception);
            SafeUi(() =>
            {
                _syncStatus.Text = "Couldn’t sync. It will retry.";
                _syncStatus.ForeColor = Color.Firebrick;
            });
        }
    }

    private void SaveWebSyncSettings()
    {
        _settings.SyncEnabled = _syncEnabled.Checked;
        _settings.WebAppBaseUrl = _webAppUrl.Text.Trim();
        _settings.IngestToken = _ingestToken.Text.Trim();
        SettingsStore.Save(_settings);
    }

    private async Task TestWebConnectionAsync(Button testButton)
    {
        SaveWebSyncSettings();
        testButton.Enabled = false;
        _syncStatus.Text = "Testing…";
        _syncStatus.ForeColor = Color.FromArgb(72, 76, 94);
        try
        {
            await ResultSyncClient.TestConnectionAsync(_settings);
            _syncStatus.Text = "Connected";
            _syncStatus.ForeColor = Color.FromArgb(29, 126, 77);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("Web sync connection test failed.", exception);
            _syncStatus.Text = "Couldn’t connect.";
            _syncStatus.ForeColor = Color.Firebrick;
        }
        finally
        {
            testButton.Enabled = true;
        }
    }

    private async Task RetryPendingResultsAsync()
    {
        if (!_settings.SyncEnabled)
        {
            return;
        }

        try
        {
            var count = await ResultSyncClient.RetryPendingAsync(_settings);
            if (count > 0)
            {
                SafeUi(() =>
                {
                    _syncStatus.Text = $"Synced {count} pending result(s).";
                    _syncStatus.ForeColor = Color.FromArgb(29, 126, 77);
                });
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("Retrying pending web results failed.", exception);
            SafeUi(() =>
            {
                _syncStatus.Text = "Waiting to sync.";
                _syncStatus.ForeColor = Color.Firebrick;
            });
        }
    }

    private void HandleCaptureError(string message)
    {
        DiagnosticLog.Warning($"Capture engine error: {message}");
        SafeUi(() => UpdateMonitoringStatus("Capture issue. It will retry.", Color.Firebrick));
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void UpdateReferenceStatus()
    {
        if (_settings.HasReference)
        {
            _referenceStatus.Text = "Configured";
            _referenceStatus.ForeColor = Color.FromArgb(29, 126, 77);
        }
        else
        {
            _referenceStatus.Text = "Not configured";
            _referenceStatus.ForeColor = Color.FromArgb(174, 83, 38);
        }
    }

    private void UpdateMonitoringStatus(string text, Color color)
    {
        _monitoringStatus.Text = text;
        _monitoringStatus.ForeColor = color;
    }

    private void OpenCaptureDirectory()
    {
        try
        {
            Directory.CreateDirectory(_settings.CaptureDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _settings.CaptureDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("Opening the capture directory failed.", exception);
            MessageBox.Show(
                "Couldn’t open the screenshot folder.",
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void CreateSupportBundle()
    {
        try
        {
            SaveWebSyncSettings();
            var path = SupportBundle.Create(_settings);
            MessageBox.Show(
                "Debug log saved. The latest screenshot is included.",
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetDirectoryName(path)!,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("Support bundle creation failed.", exception);
            MessageBox.Show(
                "Couldn’t create the debug log. Please try again.",
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task UploadSupportBundleAsync(Button? sourceButton = null)
    {
        SaveWebSyncSettings();
        var confirmation = MessageBox.Show(
            this,
            "Send the latest screenshot and debug log to VRena support?",
            "Send debug log",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirmation != DialogResult.OK)
        {
            return;
        }

        if (sourceButton is not null)
        {
            sourceButton.Enabled = false;
            sourceButton.Text = "Uploading…";
        }

        try
        {
            var path = SupportBundle.Create(_settings);
            var receipt = await SupportBundleUploadClient.UploadAsync(_settings, path);
            DiagnosticLog.Info($"Support bundle uploaded. BundleId={receipt.BundleId}; Sha256={receipt.Sha256}");
            MessageBox.Show(
                this,
                "Debug log sent.",
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("Support bundle upload failed.", exception);
            MessageBox.Show(
                this,
                "Couldn’t send the debug log. Please try again.",
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (sourceButton is not null)
            {
                sourceButton.Enabled = true;
                sourceButton.Text = "Upload support bundle";
            }
        }
    }

    private async Task CheckForUpdatesAsync(bool silentWhenCurrent, Button? sourceButton = null)
    {
        SaveWebSyncSettings();
        if (sourceButton is not null)
        {
            sourceButton.Enabled = false;
            sourceButton.Text = "Checking…";
        }

        try
        {
            var result = await UpdateService.CheckAsync(_settings);
            if (!result.IsUpdateAvailable)
            {
                if (!silentWhenCurrent)
                {
                    MessageBox.Show(
                        this,
                        $"VRena Results Capture {result.CurrentVersion.ToString(3)} is up to date.",
                        AppPaths.ProductName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            ShowFromTray();
            var response = MessageBox.Show(
                this,
                $"Version {result.AvailableVersion.ToString(3)} is ready. Install it now?",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (response != DialogResult.Yes)
            {
                return;
            }

            if (sourceButton is not null)
            {
                sourceButton.Text = "Downloading…";
            }
            await UpdateService.DownloadAndInstallAsync(result.Manifest);
            _allowClose = true;
            Close();
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("Update check or installation failed.", exception);
            if (!silentWhenCurrent)
            {
                MessageBox.Show(
                    this,
                    "Couldn’t install the update. Please try again.",
                    AppPaths.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (sourceButton is not null && !sourceButton.IsDisposed)
            {
                sourceButton.Enabled = true;
                sourceButton.Text = "Check for updates";
            }
        }
    }

    private Screen? GetSelectedScreen() =>
        (_monitorSelector.SelectedItem as ScreenChoice)?.Screen ?? Screen.PrimaryScreen;

    private static Icon LoadApplicationIcon()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var embedded = Icon.ExtractAssociatedIcon(path);
            if (embedded is not null)
            {
                return embedded;
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        if (!SetWindowDisplayAffinity(Handle, WindowDisplayAffinityExcludeFromCapture))
        {
            DiagnosticLog.Warning(
                "The application window could not be excluded from screenshots. " +
                $"WindowsError={Marshal.GetLastWin32Error()}");
        }
        else
        {
            DiagnosticLog.Info("Application window excluded from result screenshots.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr windowHandle, uint affinity);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine?.Dispose();
            _trayIcon.Dispose();
            _applicationIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class ScreenChoice
    {
        internal Screen Screen { get; }

        internal ScreenChoice(Screen screen)
        {
            Screen = screen;
        }

        public override string ToString()
        {
            var primary = Screen.Primary ? " · Primary" : string.Empty;
            return $"{Screen.DeviceName} · {Screen.Bounds.Width} × {Screen.Bounds.Height}{primary}";
        }
    }

    private sealed class MonitorEngineForManualCapture : IDisposable
    {
        private readonly CaptureSettings _settings;
        private readonly Screen _screen;

        internal MonitorEngineForManualCapture(CaptureSettings settings, Screen? screen)
        {
            _settings = settings;
            _screen = screen ?? throw new InvalidOperationException("No display is available.");
        }

        internal string Capture()
        {
            if (AppPaths.IsInsideInstallDirectory(_settings.CaptureDirectory))
            {
                throw new InvalidOperationException(
                    "Choose a capture folder outside the application installation folder.");
            }

            var timestamp = DateTimeOffset.Now;
            var directory = Path.Combine(
                _settings.CaptureDirectory,
                timestamp.ToString("yyyy"),
                timestamp.ToString("MM"));
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"VRena_Result_{timestamp:yyyy-MM-dd_HH-mm-ss-fff}.png");
            using var screenshot = ScreenshotHelper.CaptureScreen(_screen);
            screenshot.Save(path, ImageFormat.Png);

            var logPath = Path.Combine(_settings.CaptureDirectory, "capture-log.csv");
            if (!File.Exists(logPath))
            {
                File.AppendAllText(logPath, "captured_at_local,utc_offset,monitor,file\r\n");
            }

            var relative = Path.GetRelativePath(_settings.CaptureDirectory, path);
            File.AppendAllText(
                logPath,
                $"\"{timestamp:yyyy-MM-dd HH:mm:ss.fff}\",\"{timestamp:zzz}\",\"{_screen.DeviceName}\",\"{relative}\"\r\n");
            return path;
        }

        public void Dispose()
        {
        }
    }
}
