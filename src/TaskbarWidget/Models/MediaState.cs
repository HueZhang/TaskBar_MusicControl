using System.Windows.Media.Imaging;

namespace TaskbarWidget.Models;

public sealed class MediaState
{
    public bool HasMedia { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public BitmapImage? Artwork { get; set; }
    public bool IsPlaying { get; set; }
    public bool CanPlayPause { get; set; }
    public bool CanNext { get; set; }
    public bool CanPrevious { get; set; }
}
