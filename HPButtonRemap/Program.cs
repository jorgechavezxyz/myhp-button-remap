using System.Diagnostics;
using System.Text.Json;

namespace HPButtonRemap;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Must run before any window (including the bootstrapper's message box) is created.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        if (SelfBootstrapper.TryInstallAndRelaunch(args))
        {
            return;
        }

        // Open the signal before acquiring the mutex so a simultaneous launch can always signal it.
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HPButtonRemap.Settings.9B7E50F7");
        using var instance = new Mutex(true, AppPaths.InstanceMutexName, out bool first);

        if (!first)
        {
            try
            {
                first = instance.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                first = true;
            }

            if (!first)
            {
                showSettings.Set();
                return;
            }
        }

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var tray = new TrayApplicationContext(
                showSettings, args.Contains("--settings", StringComparer.OrdinalIgnoreCase));
            Application.Run(tray);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }
}

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ActionExecutor _executor;
    private readonly ConfigStore _configStore;
    private readonly StartupShortcutManager _startupShortcutManager;
    private readonly ToolStripMenuItem _runAtStartupItem;

    private WmiEventMonitor? _monitor;
    private Config? _currentConfig;
    private SettingsForm? _settings;
    private readonly Control _ui = new();
    private readonly RegisteredWaitHandle _settingsWait;
    private readonly System.Windows.Forms.Timer _monitorRetry = new() { Interval = 10_000 };
    private volatile bool _paused;
    private int _actionRunning;
    private string? _lastError;
    private long _lastErrorTime;
    
    private static Icon LoadTrayIcon()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        // Matches: <RootNamespace>.<filename>  e.g. HPButtonRemap.tray.ico
        using var stream = assembly.GetManifestResourceStream("HPButtonRemap.tray.ico");
        return stream is not null ? new Icon(stream) : SystemIcons.Application;
    }

    public TrayApplicationContext(EventWaitHandle settingsSignal, bool showSettings)
    {
        _executor = new ActionExecutor();
        _configStore = new ConfigStore(AppPaths.ConfigPath);
        _startupShortcutManager = new StartupShortcutManager();

        _runAtStartupItem = new ToolStripMenuItem("Run at startup")
        {
            CheckOnClick = true
        };
        _runAtStartupItem.Click += (_, _) => ToggleRunAtStartup();

        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            ContextMenuStrip = CreateContextMenu(),
            Visible = true,
            Text = "HP Button Remap"
        };

        _trayIcon.DoubleClick += (_, _) => OpenSettings();
        _monitorRetry.Tick += (_, _) => RetryMonitor();
        _ = _ui.Handle;
        _settingsWait = ThreadPool.RegisterWaitForSingleObject(settingsSignal,
            (_, _) => PostToTray(OpenSettings), null, Timeout.Infinite, executeOnlyOnce: false);
        ReloadConfiguration();
        if (showSettings)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 1 };
            timer.Tick += (_, _) =>
            {
                timer.Dispose();
                OpenSettings();
            };
            timer.Start();
        }
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        var pause = new ToolStripMenuItem("Pause remapping") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) =>
        {
            _paused = pause.Checked;
            _trayIcon.Text = _paused ? "HP Button Remap (paused)" : "HP Button Remap";
        };
        menu.Items.Add(pause);

        var openConfigItem = new ToolStripMenuItem("Open Configuration");
        openConfigItem.Click += (_, _) => OpenConfiguration();
        menu.Items.Add(openConfigItem);

        var reloadItem = new ToolStripMenuItem("Reload Configuration");
        reloadItem.Click += (_, _) => ReloadConfiguration();
        menu.Items.Add(reloadItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_runAtStartupItem);

        menu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("About");
        aboutItem.Click += (_, _) => ShowAbout();
        menu.Items.Add(aboutItem);

        var uninstallItem = new ToolStripMenuItem("Uninstall...");
        uninstallItem.Click += (_, _) => ShowUninstall();
        if (!AppPaths.IsMsiManaged)
            menu.Items.Add(uninstallItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ReloadConfiguration()
    {
        if (_settings is not null)
        {
            Notify("Close Settings before reloading configuration.");
            return;
        }

        Config candidate;
        try
        {
            candidate = _configStore.LoadOrCreateDefault();
        }
        catch (Exception ex)
        {
            Notify(_currentConfig is null
                ? $"Could not read config.json. Use Open Configuration to fix it, then Reload Configuration. {ex.Message}"
                : $"Could not reload settings. The previous mapping is still active. {ex.Message}");
            return;
        }

        bool eventChanged = _monitor is null ||
            candidate.EffectiveEventID != _currentConfig?.EffectiveEventID ||
            candidate.EffectiveEventData != _currentConfig?.EffectiveEventData;

        if (eventChanged && !TryStartMonitor(candidate, out string? error))
        {
            if (_monitor is not null)
            {
                Notify($"Could not reload settings. The previous mapping is still active. {error}");
                return;
            }

            // Nothing is listening yet, usually because HP's WMI provider is not ready right after sign-in.
            // Keep the configuration so Settings still works, and retry in the background.
            ApplyConfiguration(candidate);
            _monitorRetry.Start();
            Notify($"Waiting for the HP key service. Retrying in the background. {error}");
            return;
        }

        ApplyConfiguration(candidate);
        ShowReadyNotification();
    }

    private void ApplyConfiguration(Config config)
    {
        _currentConfig = config;
        _monitor?.UpdateAction(config.Action);
        _runAtStartupItem.Checked = config.RunAtStartup;
        try
        {
            SetStartupEnabled(config.RunAtStartup);
        }
        catch (Exception ex)
        {
            Notify($"Could not update the startup shortcut. {ex.Message}");
        }
    }

    private void ShowReadyNotification()
    {
        if (_currentConfig is not { ShowStartupNotification: true } config)
            return;

        string message = config.Action is null ? "Choose an action in Settings." : "The HP logo key is ready.";
        _trayIcon.ShowBalloonTip(2000, "HP Button Remap", message, ToolTipIcon.Info);
    }

    private bool TryStartMonitor(Config config, out string? error)
    {
        var replacement = new WmiEventMonitor(RunHotkeyAction, OnMonitorStopped);
        try
        {
            replacement.StartMonitoring(config);
        }
        catch (Exception ex)
        {
            replacement.Dispose();
            error = ex.Message;
            return false;
        }

        _monitor?.Dispose();
        _monitor = replacement;
        _monitor.Activate();
        _monitorRetry.Stop();
        error = null;
        return true;
    }

    private void RetryMonitor()
    {
        if (_monitor is not null || _currentConfig is not { } config)
        {
            _monitorRetry.Stop();
            return;
        }

        if (TryStartMonitor(config, out _))
            ShowReadyNotification();
    }

    // Called on a WMI thread when the subscription ends without being stopped, for example
    // after the WMI service or HP's event provider restarts.
    private void OnMonitorStopped(WmiEventMonitor stopped) => PostToTray(() =>
    {
        if (!ReferenceEquals(_monitor, stopped))
            return;

        stopped.Dispose();
        _monitor = null;
        _monitorRetry.Start();
        Notify("The HP key service stopped. Reconnecting in the background.");
    });

    private void RunHotkeyAction(ButtonAction action)
    {
        if (_paused || Interlocked.Exchange(ref _actionRunning, 1) != 0)
            return;

        try
        {
            if (_executor.ExecuteAction(action) is { } error)
                PostToTray(() => Notify(error));
        }
        finally
        {
            Volatile.Write(ref _actionRunning, 0);
        }
    }

    private void PostToTray(Action callback)
    {
        try
        {
            _ui.BeginInvoke(callback);
        }
        catch (InvalidOperationException)
        {
            // The tray is shutting down.
        }
    }

    private void Notify(string message)
    {
        long now = Environment.TickCount64;
        if (message == _lastError && now - _lastErrorTime < 8000)
            return;

        _lastError = message;
        _lastErrorTime = now;
        _trayIcon.ShowBalloonTip(4000, "HP Button Remap", message, ToolTipIcon.Warning);
    }

    private void SetStartupEnabled(bool enabled) =>
        _startupShortcutManager.SetStartupEnabled(enabled, AppPaths.ExecutablePath, AppPaths.AppDirectory);

    private void ToggleRunAtStartup()
    {
        if (_currentConfig == null)
        {
            _runAtStartupItem.Checked = false;
            Notify("Fix config.json before changing startup settings.");
            return;
        }

        bool previous = _currentConfig.RunAtStartup;
        _currentConfig.RunAtStartup = _runAtStartupItem.Checked;
        try
        {
            SetStartupEnabled(_currentConfig.RunAtStartup);
            _configStore.Save(_currentConfig);
        }
        catch (Exception ex)
        {
            _currentConfig.RunAtStartup = previous;
            _runAtStartupItem.Checked = previous;
            try
            {
                SetStartupEnabled(previous);
            }
            catch (Exception rollbackError)
            {
                Notify($"Could not restore the startup shortcut. {rollbackError.Message}");
                return;
            }
            Notify($"Could not change startup settings. {ex.Message}");
        }
    }

    private void OpenSettings()
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }

        if (_currentConfig is not { } config)
        {
            Notify("config.json could not be read. Use Open Configuration to fix it, then Reload Configuration.");
            return;
        }

        using var settings = new SettingsForm(config.Action, _executor, action =>
        {
            var previous = config.Action;
            config.Action = action;
            try
            {
                _configStore.Save(config);
            }
            catch
            {
                config.Action = previous;
                throw;
            }
            _monitor?.UpdateAction(action);
        });

        _settings = settings;
        try
        {
            settings.ShowDialog();
        }
        finally
        {
            _settings = null;
        }
    }

    private static void OpenConfiguration()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.ConfigPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to open configuration file: {ex.Message}",
                "HP Button Remap Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void ShowAbout()
    {
        string statusText = _currentConfig is { Action: not null }
            ? $"Status: Monitoring EventID={_currentConfig.EffectiveEventID}, EventData={_currentConfig.EffectiveEventData} ({_currentConfig.Action.Type})"
            : "Status: No actions configured";

        MessageBox.Show(
            $"HP Button Remap {typeof(Program).Assembly.GetName().Version?.ToString(3)}\n\n" +
            "Actions: OpenFile, OpenWebsite, SendShortcut, RunCommand\n\n" +
            statusText + "\n\n" +
            "Config: " + AppPaths.ConfigPath + "\n\n" +
            "Right-click the tray icon to access options.",
            "About HP Button Remap",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
    }

    private void ShowUninstall()
    {
        var result = MessageBox.Show(
            "This removes HP Button Remap and its startup shortcut.\n\n" +
            "Your config will be backed up to the Desktop before removal.\n\n" +
            "Continue?",
            "Uninstall HP Button Remap",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        );

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            PerformUninstall();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error during uninstall: {ex.Message}",
                "Uninstall Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void PerformUninstall()
    {
        _startupShortcutManager.SetStartupEnabled(false, AppPaths.ExecutablePath, AppPaths.AppDirectory);

        // Only self-delete if running from the managed install directory.
        if (!AppPaths.IsRunningFromManagedInstallDirectory)
        {
            MessageBox.Show(
                "Startup registration has been removed.\n\n" +
                "This app is running in portable mode; delete its folder manually to finish uninstall.",
                "Portable Mode",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            ExitApplication();
            return;
        }

        if (File.Exists(AppPaths.ConfigPath))
        {
            string backupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "HPButtonRemap-config-backup.json"
            );
            File.Copy(AppPaths.ConfigPath, backupPath, true);
        }

        string batchPath = Path.Combine(Path.GetTempPath(), "HPButtonRemap-uninstall.bat");
        string batchContent = $@"@echo off
timeout /t 2 /nobreak >nul
rd /s /q ""{AppPaths.InstallDirectory}""
del ""{batchPath}""
";
        File.WriteAllText(batchPath, batchContent);

        Process.Start(new ProcessStartInfo
        {
            FileName = batchPath,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        MessageBox.Show(
            "Uninstall started. The app will now close and remove its installed files.",
            "Uninstall Complete",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );

        ExitApplication();
    }

    private void ExitApplication()
    {
        _monitorRetry.Stop();
        _monitor?.Dispose();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settingsWait.Unregister(null);
            _monitorRetry.Dispose();
            _monitor?.Dispose();
            _ui.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

public static class AppPaths
{
    public static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HPButtonRemap"
    );

    public const string InstanceMutexName = @"Local\HPButtonRemap.Instance.9B7E50F7";

    public static readonly string InstalledExecutablePath = Path.Combine(InstallDirectory, "HPButtonRemap.exe");

    public static string AppDirectory => Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
    public static string ExecutablePath => Environment.ProcessPath ?? InstalledExecutablePath;
    public static bool IsMsiManaged { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "msi-managed.txt"));
    public static string ConfigPath => IsMsiManaged
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HPButtonRemapGUI", "config.json")
        : Path.Combine(AppDirectory, "config.json");
    public static string StartupShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        IsMsiManaged ? "HP Button Remap GUI.lnk" : "HP Button Remap.lnk"
    );

    public static bool IsRunningFromManagedInstallDirectory =>
        string.Equals(
            NormalizePath(AppDirectory),
            NormalizePath(InstallDirectory),
            StringComparison.OrdinalIgnoreCase
        );

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _configPath;

    public ConfigStore(string configPath)
    {
        _configPath = configPath;
    }

    public Config LoadOrCreateDefault()
    {
        if (!File.Exists(_configPath))
        {
            var defaults = CreateDefaultConfig();
            Save(defaults);
            return defaults;
        }

        string json = File.ReadAllText(_configPath);
        var config = JsonSerializer.Deserialize<Config>(json, JsonOptions)
            ?? throw new JsonException("The configuration must be a JSON object.");
        if (config.EffectiveEventID < 0 || config.EffectiveEventData < 0)
            throw new JsonException("EventID and EventData must be non-negative.");

        if (config.Action is { } action)
        {
            bool isValid = action.Type switch
            {
                ActionType.OpenFile => action.FilePath is { } path && Path.IsPathFullyQualified(path),
                ActionType.OpenWebsite => ButtonAction.TryGetWebsite(action.OpenWebsite, out _),
                ActionType.SendShortcut => KeyboardShortcut.TryParse(action.Shortcut, out _),
                ActionType.RunCommand => !string.IsNullOrWhiteSpace(action.Command),
                _ => false
            };

            if (!isValid)
                throw new JsonException("The configured action is incomplete or invalid.");
        }

        return config;
    }

    public void Save(Config config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        string json = JsonSerializer.Serialize(config, JsonOptions);
        string temporaryPath = _configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _configPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static Config CreateDefaultConfig() => new()
    {
        ShowStartupNotification = true,
        RunAtStartup = !AppPaths.IsMsiManaged,
        EventID = 29,
        EventData = 8616,
        Action = AppPaths.IsMsiManaged ? null : ButtonAction.CreateDefault()
    };
}

public sealed class StartupShortcutManager
{
    public void SetStartupEnabled(bool enabled, string targetPath, string workingDirectory)
    {
        if (!enabled)
        {
            if (File.Exists(AppPaths.StartupShortcutPath))
            {
                File.Delete(AppPaths.StartupShortcutPath);
            }
            return;
        }

        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is not available.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)!;
            shortcut = ((dynamic)shell).CreateShortcut(AppPaths.StartupShortcutPath);
            dynamic link = shortcut!;
            link.TargetPath = targetPath;
            link.Arguments = "--portable";
            link.WorkingDirectory = workingDirectory;
            link.Description = "HP Button Remap";
            link.Save();
        }
        finally
        {
            if (shortcut is not null)
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
            if (shell is not null)
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }
}

public static class SelfBootstrapper
{
    public static bool TryInstallAndRelaunch(string[] args)
    {
#if DEBUG
        return false;
#else
        if (AppPaths.IsMsiManaged || args.Any(arg => string.Equals(arg, "--portable", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string currentExe = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentExe))
        {
            return false;
        }

        string currentPath = Path.GetFullPath(currentExe);
        string installedPath = Path.GetFullPath(AppPaths.InstalledExecutablePath);

        if (string.Equals(currentPath, installedPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // An installed copy that is running cannot be overwritten. Without this check the copy fails,
        // the download keeps running from its own folder, and the installed version never updates.
        if (File.Exists(installedPath) && !FilesMatch(currentPath, installedPath) && IsAnotherCopyRunning())
        {
            MessageBox.Show(
                "HP Button Remap is already running. Exit it from the tray icon, then run this file again to install the update.",
                "HP Button Remap",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return true;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.InstallDirectory);
            if (!File.Exists(installedPath) || !FilesMatch(currentPath, installedPath))
                File.Copy(currentPath, installedPath, true);

            string sourceConfig = Path.Combine(Path.GetDirectoryName(currentPath)!, "config.json");
            string targetConfig = Path.Combine(AppPaths.InstallDirectory, "config.json");
            if (File.Exists(sourceConfig) && !File.Exists(targetConfig))
            {
                File.Copy(sourceConfig, targetConfig, false);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = installedPath,
                UseShellExecute = true,
                WorkingDirectory = AppPaths.InstallDirectory
            };
            if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
                startInfo.ArgumentList.Add("--settings");

            using var process = Process.Start(startInfo);

            return true;
        }
        catch
        {
            // If installation bootstrap fails, continue in portable mode.
            return false;
        }

#endif
    }

    private static bool FilesMatch(string first, string second)
    {
        try
        {
            if (new FileInfo(first).Length != new FileInfo(second).Length)
                return false;

            using var a = File.OpenRead(first);
            using var b = File.OpenRead(second);
            return System.Security.Cryptography.SHA256.HashData(a)
                .AsSpan()
                .SequenceEqual(System.Security.Cryptography.SHA256.HashData(b));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsAnotherCopyRunning()
    {
        if (!Mutex.TryOpenExisting(AppPaths.InstanceMutexName, out var mutex))
            return false;
        mutex.Dispose();
        return true;
    }
}
