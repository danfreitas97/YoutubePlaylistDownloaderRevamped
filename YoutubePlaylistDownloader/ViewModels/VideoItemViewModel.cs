namespace YoutubePlaylistDownloader.ViewModels;

/// <summary>
/// A video in the "pick what to download" list.
/// </summary>
public partial class VideoItemViewModel(IVideo video, int index, FullPlaylist playlist, Action onSelectionChanged) : ObservableObject
{
    public IVideo Video { get; } = video;

    /// <summary>Zero-based position in its playlist or channel.</summary>
    public int Index { get; } = index;

    public FullPlaylist Playlist { get; } = playlist;

    public int Number => Index + 1;
    public string Title => Video.Title;
    public string Channel => Video.Author?.ChannelTitle;
    public string ThumbnailUrl => LinkResolver.Thumbnail(Video.Id);
    public string Duration => Video.Duration is TimeSpan d ? Format.Duration(d) : string.Empty;
    public bool HasDuration => Video.Duration.HasValue;

    [ObservableProperty]
    private bool isSelected = true;

    /// <summary>A file with the name this video would get already exists in the destination folder.</summary>
    [ObservableProperty]
    private bool exists;

    partial void OnIsSelectedChanged(bool value) => onSelectionChanged?.Invoke();
}
