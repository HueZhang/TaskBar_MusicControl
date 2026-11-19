using System.Runtime.InteropServices;
using System.Windows.Threading;
using TaskbarWidget.Interop;

namespace TaskbarWidget.Services;

/// <summary>
/// Global low-level mouse hook. Because WM_MOUSEWHEEL is delivered to the focus window
/// (and our embedded widget normally has no focus), we hook at the LL level and check
/// whether the cursor is inside the widget rectangle. Only then do we swallow the message
/// and ask the UI to switch pages; otherwise the event is passed through untouched.
/// </summary>
internal sealed class WheelSwitchService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isWidgetVisible;
    private readonly Func<NativeMethods.RECT> _getBounds;
    private readonly Func<bool> _canSwitch;
    private readonly Action<int> _onWheel; // receives the wheel delta sign (+1 up, -1 down)
    private readonly SettingsService _settings;

    private nint _hookId;
    private NativeMethods.LowLevelMouseProc? _proc; // keep delegate alive for the hook lifetime

    public WheelSwitchService(
        Dispatcher dispatcher,
        SettingsService settings,
        Func<bool> isWidgetVisible,
        Func<NativeMethods.RECT> getBounds,
        Func<bool> canSwitch,
        Action<int> onWheel)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _isWidgetVisible = isWidgetVisible;
        _getBounds = getBounds;
        _canSwitch = canSwitch;
        _onWheel = onWheel;
    }

    public void Install()
    {
        if (_hookId != 0) return;

        _proc = HookCallback;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        nint hMod = NativeMethods.GetModuleHandle(module?.ModuleName);
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
        if (_hookId == 0)
            _proc = null; // installation failed; don't hold the delegate
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= NativeMethods.HC_ACTION && wParam == (nint)NativeMethods.WM_MOUSEWHEEL)
        {
            try
            {
                if (_isWidgetVisible() && _canSwitch())
                {
                    var hook = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    var r = _getBounds();
                    if (hook.pt.X >= r.Left && hook.pt.X <= r.Right &&
                        hook.pt.Y >= r.Top && hook.pt.Y <= r.Bottom)
                    {
                        // High-order word of mouseData is the wheel delta (120 per notch).
                        short delta = unchecked((short)(hook.mouseData >> 16));
                        int direction = delta > 0 ? 1 : -1;
                        // Dispatcher.BeginInvoke keeps the hook callback fast and avoids
                        // touching WPF objects from the hook thread.
                        _dispatcher.BeginInvoke(() => _onWheel(direction));
                        return 1; // swallow: the widget consumed this wheel event
                    }
                }
            }
            catch
            {
                // Never let an exception escape a low-level hook — that would break input.
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Uninstall()
    {
        if (_hookId != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = 0;
        }
        _proc = null;
    }

    public void Dispose() => Uninstall();
}
