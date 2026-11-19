using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskbarWidget.Models;
using TaskbarWidget.Services;

namespace TaskbarWidget.ViewModels;

public sealed partial class WidgetViewModel : ObservableObject
{
    private readonly MediaService _media;
    private readonly SystemMonitorService _monitor;
    private readonly SettingsService _settings;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    // --- Monitor page ---
    [ObservableProperty] private double _uploadBps;
    [ObservableProperty] private double _downloadBps;
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private string _memoryTooltip = string.Empty;

    // --- Media page ---
    [ObservableProperty] private string _title = "无媒体播放";
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private BitmapImage? _artwork;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _mediaAvailable;
    [ObservableProperty] private bool _canPlayPause;
    [ObservableProperty] private bool _canNext;
    [ObservableProperty] private bool _canPrevious;

    // --- Media flyout ---
    public event EventHandler? OpenMediaFlyoutRequested;

    // --- Page switching ---
    [ObservableProperty] private int _activePage; // 0 = monitor, 1 = media

    public bool MonitorActive => ActivePage == 0;
    public bool MediaActive => ActivePage == 1;
    public bool MediaDotEnabled => MediaAvailable;

    public WidgetViewModel(MediaService media, SystemMonitorService monitor, SettingsService settings)
    {
        _media = media;
        _monitor = monitor;
        _settings = settings;

        _media.StateChanged += OnMediaStateChanged;
        _media.AvailabilityChanged += OnMediaAvailabilityChanged;
        _monitor.StatsUpdated += OnStatsUpdated;
    }

    private void OnStatsUpdated(object? sender, SystemStats stats)
    {
        if (_dispatcher.CheckAccess())
            ApplyStats(stats);
        else
            _dispatcher.Invoke(() => ApplyStats(stats));
    }

    private void ApplyStats(SystemStats stats)
    {
        UploadBps = stats.UploadBps;
        DownloadBps = stats.DownloadBps;
        CpuPercent = stats.CpuPercent;
        MemoryPercent = stats.MemoryPercent;
        MemoryTooltip = $"内存 {stats.MemoryUsedGb:0.0} / {stats.MemoryTotalGb:0.0} GB";
    }

    private void OnMediaStateChanged(object? sender, MediaState state)
    {
        if (_dispatcher.CheckAccess())
            ApplyMediaState(state);
        else
            _dispatcher.Invoke(() => ApplyMediaState(state));
    }

    private void ApplyMediaState(MediaState state)
    {
        Title = string.IsNullOrWhiteSpace(state.Title) ? "无媒体播放" : state.Title;
        Artist = state.Artist;
        Artwork = state.Artwork;
        IsPlaying = state.IsPlaying;
        CanPlayPause = state.CanPlayPause;
        CanNext = state.CanNext;
        CanPrevious = state.CanPrevious;
    }

    private void OnMediaAvailabilityChanged(object? sender, bool available)
    {
        if (_dispatcher.CheckAccess())
            ApplyAvailability(available);
        else
            _dispatcher.Invoke(() => ApplyAvailability(available));
    }

    private void ApplyAvailability(bool available)
    {
        MediaAvailable = available;
        OnPropertyChanged(nameof(MediaDotEnabled));
        if (!available && ActivePage == 1)
            SwitchToMonitor();
    }

    partial void OnActivePageChanged(int value)
    {
        OnPropertyChanged(nameof(MonitorActive));
        OnPropertyChanged(nameof(MediaActive));
    }

    /// <summary>Toggle between the two pages. Only works when media is available.</summary>
    public void TogglePage()
    {
        if (!MediaAvailable) return;
        ActivePage = ActivePage == 0 ? 1 : 0;
    }

    /// <summary>Handle a wheel event over the widget. direction: +1 = up, -1 = down.</summary>
    public void HandleWheel(int direction)
    {
        if (!MediaAvailable) return;
        if (_settings.Current.WheelMode == WheelMode.Directional)
        {
            // Up -> monitor page (0), down -> media page (1)
            ActivePage = direction > 0 ? 0 : 1;
        }
        else
        {
            TogglePage();
        }
    }

    [RelayCommand]
    private void SwitchToMonitor() => ActivePage = 0;

    [RelayCommand]
    private void SwitchToMedia()
    {
        if (MediaAvailable) ActivePage = 1;
    }

    [RelayCommand]
    private void OpenMediaFlyout() => OpenMediaFlyoutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task TogglePlayPauseAsync() => await _media.TogglePlayPauseAsync();

    [RelayCommand]
    private async Task NextAsync() => await _media.NextAsync();

    [RelayCommand]
    private async Task PreviousAsync() => await _media.PreviousAsync();

    public void Dispose()
    {
        _media.StateChanged -= OnMediaStateChanged;
        _media.AvailabilityChanged -= OnMediaAvailabilityChanged;
        _monitor.StatsUpdated -= OnStatsUpdated;
    }
}
