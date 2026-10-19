namespace YoutubePlaylistDownloader.ViewModels;

/// <summary>
/// A choice in a dropdown: a stable key plus the text shown for it.
/// </summary>
public record Option(string Key, string Label)
{
    public override string ToString() => Label;
}
