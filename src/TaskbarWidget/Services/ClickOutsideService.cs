using System.Runtime.InteropServices;
using System.Windows.Threading;
using TaskbarWidget.Interop;

namespace TaskbarWidget.Services;

/// <summary>
/// Low-level mouse hook that detects clicks outside a set of windows.
/// Used to dismiss the media flyout when the user clicks anywhere else.
/// </summary>
internal sealed class ClickOutsideService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<IReadOnlyList<nint>> _getWindowHandles;
    private readonly Action _onClickOutside;
    private nint _hookId;
    private NativeMethods.LowLevelMouseProc? _proc;

    private static readonly uint[] ClickMessages =
    {
        NativeMethods.WM_LBUTTONDOWN,
        NativeMethods.WM_RBUTTONDOWN,
        NativeMethods.WM_MBUTTONDOWN
    };

    public ClickOutsideService(
        Dispatcher dispatcher,
        Func<IReadOnlyList<nint>> getWindowHandles,
        Action onClickOutside)
    {
        _dispatcher = dispatcher;
        _getWindowHandles = getWindowHandles;
        _onClickOutside = onClickOutside;
    }

    public void Install()
    {
        if (_hookId != 0) return;
        _proc = HookCallback;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        nint hMod = NativeMethods.GetModuleHandle(module?.ModuleName);
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
        if (_hookId == 0) _proc = null;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= NativeMethods.HC_ACTION && Array.IndexOf(ClickMessages, (uint)wParam.ToInt64()) >= 0)
        {
            try
            {
                var hook = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var handles = _getWindowHandles();
                if (handles.Count == 0)
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                bool inside = false;
                foreach (nint hwnd in handles)
                {
                    if (hwnd != 0 && NativeMethods.GetWindowRect(hwnd, out var r) &&
                        hook.pt.X >= r.Left && hook.pt.X <= r.Right &&
                        hook.pt.Y >= r.Top && hook.pt.Y <= r.Bottom)
                    {
                        inside = true;
                        break;
                    }
                }
                if (!inside)
                    _dispatcher.BeginInvoke(_onClickOutside);
            }
            catch
            {
                // Never let exceptions escape a low-level hook.
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
