using WindowsMediaController;
using System.IO;
using System.Windows.Media.Imaging;
using TaskbarWidget.Models;

namespace TaskbarWidget.Services;

public sealed class MediaService : IDisposable
{
    private readonly MediaManager _manager = new();
    private BitmapImage? _cachedArtwork;
    private string? _cachedArtworkKey;

    public event EventHandler<MediaState>? StateChanged;
    public event EventHandler<bool>? AvailabilityChanged;

    public MediaState Current { get; } = new();

    public void Start()
    {
        try
        {
            _manager.OnAnySessionOpened += OnSessionsChanged;
            _manager.OnAnySessionClosed += OnSessionsChanged;
            _manager.OnFocusedSessionChanged += OnSessionsChanged;
            _manager.OnAnyMediaPropertyChanged += OnMediaPropertyChanged;
            _manager.OnAnyPlaybackStateChanged += OnPlaybackStateChanged;
            _manager.Start();
            // Defer the first read to a thread-pool thread so the UI thread never blocks
            // on the WinRT async operation during startup.
            _ = Task.Run(Refresh);
        }
        catch
        {
            // SMTC may be unavailable (service session / permission). Keep monitor page working.
            SetNoMedia();
        }
    }

    private void OnSessionsChanged(MediaManager.MediaSession session) => Refresh();

    private void OnMediaPropertyChanged(MediaManager.MediaSession sender,
        Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties args) => Refresh();

    private void OnPlaybackStateChanged(MediaManager.MediaSession sender,
        Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackInfo args) => Refresh();

    private void Refresh()
    {
        try
        {
            var session = _manager.GetFocusedSession();
            if (session?.ControlSession is null)
            {
                SetNoMedia();
                return;
            }

            var props = session.ControlSession.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();
            var info = session.ControlSession.GetPlaybackInfo();
            var controls = info?.Controls;

            bool hadMedia = Current.HasMedia;
            Current.HasMedia = !string.IsNullOrWhiteSpace(props?.Title) || !string.IsNullOrWhiteSpace(props?.Artist);
            Current.Title = props?.Title ?? string.Empty;
            Current.Artist = props?.Artist ?? string.Empty;
            Current.Album = props?.AlbumTitle ?? string.Empty;
            var newArtwork = LoadArtwork(props?.Thumbnail);
            if (!ReferenceEquals(newArtwork, Current.Artwork))
                Current.Artwork = newArtwork;
            Current.IsPlaying = info?.PlaybackStatus ==
                Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            Current.CanPlayPause = controls?.IsPlayPauseToggleEnabled ?? true;
            Current.CanNext = controls?.IsNextEnabled ?? true;
            Current.CanPrevious = controls?.IsPreviousEnabled ?? true;

            StateChanged?.Invoke(this, Current);
            if (hadMedia != Current.HasMedia)
                AvailabilityChanged?.Invoke(this, Current.HasMedia);
        }
        catch
        {
            SetNoMedia();
        }
    }

    private void SetNoMedia()
    {
        bool hadMedia = Current.HasMedia;
        Current.HasMedia = false;
        Current.Title = string.Empty;
        Current.Artist = string.Empty;
        Current.Album = string.Empty;
        Current.Artwork = null;
        Current.IsPlaying = false;
        Current.CanPlayPause = false;
        Current.CanNext = false;
        Current.CanPrevious = false;
        StateChanged?.Invoke(this, Current);
        if (hadMedia)
            AvailabilityChanged?.Invoke(this, false);
    }

    /// <summary>Convert the SMTC thumbnail stream into a WPF BitmapImage.</summary>
    private BitmapImage? LoadArtwork(Windows.Storage.Streams.IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var stream = reference.OpenReadAsync().AsTask().GetAwaiter().GetResult();
            if (stream is null || stream.Size == 0) return null;

            // Cache by the stream's content hash so repeated refreshes don't recreate the image.
            var key = $"{stream.Size}:{Current.Title}:{Current.Artist}";
            if (_cachedArtwork is not null && _cachedArtworkKey == key) return _cachedArtwork;

            using var mem = new MemoryStream();
            stream.AsStreamForRead().CopyTo(mem);
            mem.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = mem;
            image.EndInit();
            image.Freeze();

            _cachedArtwork = image;
            _cachedArtworkKey = key;
            return image;
        }
        catch
        {
            return null;
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        try
        {
            var s = _manager.GetFocusedSession()?.ControlSession;
            if (s is not null)
                await s.TryTogglePlayPauseAsync();
        }
        catch { /* no-op */ }
    }

    public async Task NextAsync()
    {
        try
        {
            var s = _manager.GetFocusedSession()?.ControlSession;
            if (s is not null)
                await s.TrySkipNextAsync();
        }
        catch { /* no-op */ }
    }

    public async Task PreviousAsync()
    {
        try
        {
            var s = _manager.GetFocusedSession()?.ControlSession;
            if (s is not null)
                await s.TrySkipPreviousAsync();
        }
        catch { /* no-op */ }
    }

    public void Dispose()
    {
        _manager.OnAnySessionOpened -= OnSessionsChanged;
        _manager.OnAnySessionClosed -= OnSessionsChanged;
        _manager.OnFocusedSessionChanged -= OnSessionsChanged;
        _manager.OnAnyMediaPropertyChanged -= OnMediaPropertyChanged;
        _manager.OnAnyPlaybackStateChanged -= OnPlaybackStateChanged;
    }
}
