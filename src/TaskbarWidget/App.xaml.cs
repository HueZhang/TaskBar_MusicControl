using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarWidget.Interop;
using TaskbarWidget.Services;
using TaskbarWidget.ViewModels;
using TaskbarWidget.Views;
using Wpf.Ui.Appearance;

namespace TaskbarWidget;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private SettingsService? _settings;
    private SystemMonitorService? _monitor;
    private MediaService? _media;
    private WidgetViewModel? _viewModel;
    private TaskbarHost? _host;
    private WheelSwitchService? _wheel;
    private ClickOutsideService? _clickOutside;
    private WidgetWindow? _widgetWindow;
    private MediaFlyoutWindow? _mediaFlyout;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance — bail out if another copy is running.
        _singleInstance = new Mutex(true, "TaskbarWidget_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        // Per-monitor DPI awareness for correct coordinates in the taskbar and mouse hook.
        SetDpiAwareness();

        _settings = new SettingsService();
        _settings.Load();
        ApplyTheme();
        _settings.SettingsChanged += (_, _) => _host?.SetPosition(_settings.Current.Position);

        _monitor = new SystemMonitorService(_settings);
        _media = new MediaService();
        _viewModel = new WidgetViewModel(_media, _monitor, _settings);

        _host = new TaskbarHost(_settings);
        _widgetWindow = new WidgetWindow(_viewModel);
        _host.Attach(_widgetWindow);
        _widgetWindow.Show();

        _mediaFlyout = new MediaFlyoutWindow(_viewModel);
        _viewModel.OpenMediaFlyoutRequested += (_, _) => ShowMediaFlyout();

        _monitor.Start();
        _media.Start();

        // Hover + wheel page switch via a low-level global mouse hook.
        _wheel = new WheelSwitchService(
            Dispatcher,
            _settings,
            () => _host.IsVisible,
            () => _host.GetBounds(),
            () => _viewModel.MediaAvailable,
            direction => _viewModel.HandleWheel(direction));
        _wheel.Install();

        _clickOutside = new ClickOutsideService(
            Dispatcher,
            GetFlyoutWindowHandles,
            () => _mediaFlyout?.CloseWithAnimation());
        _clickOutside.Install();

        SetupTray();
    }

    private IReadOnlyList<nint> GetFlyoutWindowHandles()
    {
        var list = new List<nint>();
        if (_mediaFlyout is { IsVisible: true })
            list.Add(new WindowInteropHelper(_mediaFlyout).Handle);
        return list;
    }

    private void ShowMediaFlyout()
    {
        if (_mediaFlyout is null || _widgetWindow is null || _host is null) return;
        try
        {
            nint hwnd = new WindowInteropHelper(_widgetWindow).Handle;
            double dpi = hwnd != 0 ? NativeMethods.GetDpiForWindow(hwnd) / 96.0 : 1.0;
            if (dpi <= 0) dpi = 1.0;
            var bounds = _host.GetBounds();
            _mediaFlyout.ShowAt(bounds.Left, bounds.Right, bounds.Top, dpi);
        }
        catch
        {
            _mediaFlyout.Show();
        }
    }

    private static void SetDpiAwareness()
    {
        try
        {
            NativeMethods.SetProcessDpiAwarenessContext(
                NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch { /* already set or unsupported — non-fatal */ }
    }

    private void ApplyTheme()
    {
        // Theme always follows Windows light/dark mode — no in-app theme setting.
        ApplicationThemeManager.ApplySystemTheme();
    }

    internal void RefreshSystemTheme() => ApplicationThemeManager.ApplySystemTheme();

    private static Icon LoadTrayIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("TaskbarWidget.Resources.app.ico");
        if (stream is not null) return new Icon(stream);

        // Fallback: extract from the app's own executable.
        var path = Environment.ProcessPath;
        return !string.IsNullOrEmpty(path) ? Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application
            : SystemIcons.Application;
    }

    private void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "TaskbarWidget",
            Visible = true,
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
        };

        var menu = _tray.ContextMenuStrip!;
        menu.Items.Add("设置…", null, (_, _) => OpenSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
    }

    private void OpenSettings()
    {
        if (_settings is null || _monitor is null || _host is null) return;
        var vm = new SettingsViewModel(_settings, _monitor, _host,
            onRefreshChanged: () => _monitor.RestartTimer());
        var window = new SettingsWindow(vm);
        window.Owner = Current.MainWindow;
        window.Show();
    }

    private void ExitApp()
    {
        _exitRequested = true;
        _tray!.Visible = false;
        _wheel?.Uninstall();
        _clickOutside?.Uninstall();
        _media?.Dispose();
        _monitor?.Dispose();
        if (_widgetWindow is not null && _widgetWindow.CheckAccess())
            _widgetWindow.Close();
        _tray.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_exitRequested)
        {
            _wheel?.Uninstall();
            _media?.Dispose();
            _monitor?.Dispose();
        }
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
