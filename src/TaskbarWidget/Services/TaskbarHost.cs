using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using TaskbarWidget.Interop;

namespace TaskbarWidget.Services;

/// <summary>
/// Embeds the widget window into the Windows taskbar (Shell_TrayWnd), with a
/// topmost floating-window fallback if injection fails. The taskbar injection
/// uses standard Win32 child-window APIs only — no DLL injection into Explorer.
/// </summary>
public sealed class TaskbarHost
{
    private readonly SettingsService _settings;
    private Window? _window;
    private nint _hwnd;
    private nint _trayHwnd;
    private uint _taskbarCreatedMsg;
    private bool _embedded;
    private bool _hidden;
    private bool _hooksInstalled;
    private System.Windows.Threading.DispatcherTimer? _positionTimer;
    private WidgetPosition _position = WidgetPosition.Right;

    /// <summary>
    /// Windows 11 stores taskbar alignment in HKCU\...\Advanced\TaskbarAl:
    /// 0 = left aligned, 1 = centered (default). Used to honour "left" placement
    /// only when there is actually empty space at the far-left edge.
    /// </summary>
    private static bool IsTaskbarCentered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            // TaskbarAl: 0 = left, 1 = centered (the default on Windows 11).
            var value = key?.GetValue("TaskbarAl");
            return value is null || Convert.ToInt32(value) != 0;
        }
        catch
        {
            return true; // assume centered — safer to follow app icons than overlap them
        }
    }

    private static int GetAppIconsRightEdge(nint taskbarHandle, int fallbackX)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbarHandle);
            if (root is null) return fallbackX;

            // On Windows 11 the real taskbar app buttons expose the class
            // "Taskbar.TaskListButtonAutomationPeer". TaskbarFrame itself spans the
            // WHOLE taskbar, so using it would place us near the system tray.
            var buttons = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty,
                    "Taskbar.TaskListButtonAutomationPeer"));
            if (buttons.Count > 0)
            {
                int right = 0;
                foreach (AutomationElement b in buttons)
                {
                    var r = b.Current.BoundingRectangle;
                    if (r.IsEmpty || r.Width <= 0 || b.Current.IsOffscreen) continue;
                    if (r.Right > right) right = (int)r.Right;
                }
                if (right > 0) return right;
            }

            // Fallback: any button with an "Appid:" automation id.
            var all = root.FindAll(TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);
            foreach (AutomationElement e in all)
            {
                var c = e.Current;
                if (!c.AutomationId.StartsWith("Appid:", StringComparison.Ordinal)) continue;
                var r = c.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0 || c.IsOffscreen) continue;
                return (int)r.Right;
            }

            return fallbackX;
        }
        catch
        {
            return fallbackX;
        }
    }

    public bool IsEmbedded => _embedded;
    public bool IsVisible => _window?.IsVisible == true && !_hidden;
    public nint Handle => _hwnd;

    public void SetPosition(WidgetPosition position)
    {
        _position = position;
        if (_embedded) Position();
    }

    public event EventHandler? LayoutChanged;

    public TaskbarHost(SettingsService settings)
    {
        _settings = settings;
    }

    public void Attach(Window window)
    {
        _window = window;
        _window.ShowActivated = false;
        _window.SourceInitialized += OnSourceInitialized;
        _window.Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(_window!).Handle;
        long exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        // WPF sets WS_EX_LAYERED for AllowsTransparency windows; Windows refuses to
        // embed layered windows as taskbar children, so clear that bit here.
        exStyle &= ~(long)NativeMethods.WS_EX_LAYERED;
        exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (nint)exStyle);

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
        _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        TryEmbedOrFallback();
        InstallHooks();
        _positionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _positionTimer.Tick += (_, _) => { if (_embedded) Position(); };
        _positionTimer.Start();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_taskbarCreatedMsg != 0 && msg == (int)_taskbarCreatedMsg)
        {
            // Explorer restarted — re-embed after the new taskbar is ready.
            _window!.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(600);
                TryEmbedOrFallback();
            });
        }
        else if (msg == 0x0014 /* WM_SETTINGCHANGE */ || msg == 0x0072 /* WM_DISPLAYCHANGE */)
        {
            _window!.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(200);
                if (_embedded) Position();
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        return nint.Zero;
    }

    private void InstallHooks()
    {
        if (_hooksInstalled) return;
        _hooksInstalled = true;
    }

    private void TryEmbedOrFallback()
    {
        try
        {
            _trayHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (_trayHwnd == 0)
            {
                Fallback();
                return;
            }

            // Parent directly to Shell_TrayWnd (full taskbar width). ReBarWindow32 only
            // spans the centered app buttons and would clip a widget placed near the tray.
            nint container = _trayHwnd;

            long style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE).ToInt64();
            style &= ~(long)NativeMethods.WS_POPUP;
            style |= (long)(NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPSIBLINGS);
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, (nint)style);

            NativeMethods.SetParent(_hwnd, container);
            // SetParent returns NULL on success when reparenting a top-level window,
            // so verify the actual parent rather than trusting the return value.
            if (NativeMethods.GetParent(_hwnd) != container)
            {
                Fallback();
                return;
            }

            _embedded = true;
            _position = _settings.Current.Position;
            Position();
        }
        catch
        {
            Fallback();
        }
    }

    private void Fallback()
    {
        _embedded = false;
        // Restore popup style before making it a topmost desktop window.
        try
        {
            long style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE).ToInt64();
            style &= ~(long)NativeMethods.WS_CHILD;
            style |= (long)NativeMethods.WS_POPUP;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, (nint)style);
            NativeMethods.SetParent(_hwnd, 0);
            var window = _window!;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.Topmost = true;
            window.ShowInTaskbar = false;
            Position();
        }
        catch
        {
            // Last resort: leave the window as-is.
        }
    }

    /// <summary>Positions the widget within the taskbar, or above it in fallback mode.</summary>
    public void Position()
    {
        if (_hwnd == 0 || _window is null) return;

        try
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen
                ?? throw new InvalidOperationException("No primary screen available.");
            var workArea = primary.WorkingArea;
            var screen = primary.Bounds;
            int taskbarHeight = screen.Bottom - workArea.Bottom;
            bool taskbarAtBottom = taskbarHeight > 0;
            int barH = taskbarAtBottom ? taskbarHeight : screen.Height - workArea.Height;
            if (barH <= 0) barH = 40;

            double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
            if (scale <= 0) scale = 1.0;
            int w = (int)(220 * scale);
            int h = Math.Min((int)(barH * scale), (int)(56 * scale));

            if (_embedded && _trayHwnd != 0)
            {
                NativeMethods.GetWindowRect(_trayHwnd, out var trayRect);
                int screenX;

                if (_position == WidgetPosition.Right)
                {
                    // Right: immediately left of the system tray (TrayNotifyWnd).
                    nint notify = NativeMethods.FindWindowEx(_trayHwnd, 0, "TrayNotifyWnd", null);
                    int notifyLeft = notify != 0 && NativeMethods.GetWindowRect(notify, out var nr)
                        ? nr.Left
                        : trayRect.Right - (int)(300 * scale);
                    screenX = notifyLeft - w - (int)(4 * scale);
                }
                else if (IsTaskbarCentered())
                {
                    // Windows 11 centered mode: the far-left is empty by design.
                    // Do NOT follow the app icons here (that would put us in the
                    // middle of the taskbar) — dock at the true left edge.
                    screenX = trayRect.Left + (int)(4 * scale);
                }
                else
                {
                    // Left-aligned icons: place just after the last real app icon.
                    nint rebar = NativeMethods.FindWindowEx(_trayHwnd, 0, "ReBarWindow32", null);
                    int rebarRight = rebar != 0 && NativeMethods.GetWindowRect(rebar, out var rr)
                        ? rr.Right
                        : trayRect.Left + (int)(8 * scale);
                    screenX = GetAppIconsRightEdge(_trayHwnd, rebarRight) + (int)(8 * scale);
                }
                screenX = Math.Min(screenX, trayRect.Right - w - (int)(4 * scale));
                screenX = Math.Max(trayRect.Left, screenX);
                int screenY = trayRect.Top + Math.Max(0, (trayRect.Height - h) / 2);

                // SetWindowPos on a child window expects coordinates relative to its parent.
                nint parent = NativeMethods.GetParent(_hwnd);
                var pt = new NativeMethods.POINT { X = screenX, Y = screenY };
                NativeMethods.ScreenToClient(parent == 0 ? _trayHwnd : parent, ref pt);

                // HWND_TOP puts us above Explorer's DesktopWindowContentBridge, which
                // otherwise spans the whole taskbar and hides the widget. Do NOT use
                // SWP_NOZORDER here — it would turn hWndInsertAfter into a no-op.
                NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOP, pt.X, pt.Y, w, h,
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_SHOWWINDOW |
                    NativeMethods.SWP_ASYNCWINDOWPOS);
            }
            else
            {
                // Floating fallback: sit just above the primary taskbar, right-aligned.
                int x = screen.Right - w - (int)(100 * scale);
                int y = taskbarAtBottom ? screen.Bottom - h - barH : workArea.Top;
                NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, w, h,
                    NativeMethods.SWP_NOACTIVATE);
            }

            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // ignore positioning failures
        }
    }

    internal NativeMethods.RECT GetBounds()
    {
        if (_hwnd != 0 && NativeMethods.GetWindowRect(_hwnd, out var rect))
            return rect;
        return default;
    }

    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        if (_window is null) return;
        if (hidden)
            _window.Dispatcher.Invoke(() => _window.Hide());
        else
            _window.Dispatcher.Invoke(() => { _window.Show(); Position(); });
    }
}
