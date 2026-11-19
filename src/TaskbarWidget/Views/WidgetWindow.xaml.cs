using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TaskbarWidget.Interop;
using TaskbarWidget.ViewModels;

namespace TaskbarWidget.Views;

public partial class WidgetWindow : Window
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint RegisterWindowMessage(string msg);

    private static readonly nint WM_SETTINGCHANGE_MSG = RegisterWindowMessage("ImmersiveColorSet");

    public WidgetWindow(WidgetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == (int)WM_SETTINGCHANGE_MSG || msg == 0x001A /* WM_SETTINGCHANGE */)
            ((App)System.Windows.Application.Current).RefreshSystemTheme();
        return nint.Zero;
    }
}
