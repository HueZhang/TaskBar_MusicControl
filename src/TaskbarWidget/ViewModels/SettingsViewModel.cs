using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TaskbarWidget.Services;

namespace TaskbarWidget.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly SystemMonitorService _monitor;
    private readonly TaskbarHost _host;
    private readonly Action _onRefreshChanged;

    public List<string> NicModeOptions { get; } = new() { "主网卡（自动）", "指定网卡", "全部累加" };
    public List<string> PositionOptions { get; } = new() { "右侧（贴着系统托盘）", "左侧" };

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private int _positionIndex;
    [ObservableProperty] private int _refreshIndex;
    [ObservableProperty] private int _nicModeIndex;
    [ObservableProperty] private string _selectedNicAlias = string.Empty;
    [ObservableProperty] private int _wheelModeIndex;
    [ObservableProperty] private bool _isSpecificNic;
    [ObservableProperty] private List<string> _nicAliases = new();

    public event EventHandler? RequestClose;

    public SettingsViewModel(SettingsService settings, SystemMonitorService monitor, TaskbarHost host,
        Action onRefreshChanged)
    {
        _settings = settings;
        _monitor = monitor;
        _host = host;
        _onRefreshChanged = onRefreshChanged;

        var s = settings.Current;
        StartWithWindows = s.StartWithWindows;
        PositionIndex = s.Position == WidgetPosition.Left ? 1 : 0;
        RefreshIndex = s.Refresh == RefreshInterval.TwoSeconds ? 1 : 0;
        NicModeIndex = s.NicMode switch { NicMode.Selected => 1, NicMode.All => 2, _ => 0 };
        SelectedNicAlias = s.SelectedNicAlias;
        WheelModeIndex = s.WheelMode == WheelMode.Directional ? 1 : 0;
        NicAliases = monitor.GetInterfaceAliases();
        UpdateSpecificNic();
    }

    partial void OnNicModeIndexChanged(int value) => UpdateSpecificNic();

    private void UpdateSpecificNic() => IsSpecificNic = NicModeIndex == 1;

    [RelayCommand]
    private void Save()
    {
        var s = _settings.Current;
        s.StartWithWindows = StartWithWindows;
        s.Position = PositionIndex == 1 ? WidgetPosition.Left : WidgetPosition.Right;
        bool refreshChanged = s.Refresh != (RefreshIndex == 1 ? RefreshInterval.TwoSeconds : RefreshInterval.OneSecond);
        s.Refresh = RefreshIndex == 1 ? RefreshInterval.TwoSeconds : RefreshInterval.OneSecond;
        s.NicMode = NicModeIndex switch { 1 => NicMode.Selected, 2 => NicMode.All, _ => NicMode.Primary };
        s.SelectedNicAlias = SelectedNicAlias ?? string.Empty;
        s.WheelMode = WheelModeIndex == 1 ? WheelMode.Directional : WheelMode.Toggle;

        SetStartup(StartWithWindows);
        _settings.Save();
        if (refreshChanged) _onRefreshChanged();
        _host.SetPosition(s.Position);
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null) return;
            string exe = Environment.ProcessPath ?? string.Empty;
            if (enable)
                key.SetValue("TaskbarWidget", $"\"{exe}\"");
            else
                key.DeleteValue("TaskbarWidget", false);
        }
        catch { /* registry failure is non-fatal */ }
    }
}
