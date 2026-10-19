namespace YoutubePlaylistDownloader.Services;

public enum LinkKind { None, Video, Playlist, Channel }

/// <summary>
/// One link turned into the videos it points to.
/// </summary>
public class ResolvedSource
{
    public LinkKind Kind { get; init; }
    public string Link { get; init; }
    public string Title { get; init; }
    public string Author { get; init; }
    public string ThumbnailUrl { get; init; }
    public FullPlaylist Playlist { get; init; }
    public IReadOnlyList<IVideo> Videos { get; init; } = [];
}

/// <summary>
/// Recognizes YouTube links and loads the videos behind them (video, playlist, channel by id, user or @handle).
/// </summary>
public static class LinkResolver
{
    public static string[] SplitLines(string text) =>
        (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static LinkKind Detect(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return LinkKind.None;

        if (YoutubeHelpers.TryParsePlaylistId(link, out _))
            return LinkKind.Playlist;

        if (YoutubeHelpers.TryParseChannelId(link, out _) || YoutubeHelpers.TryParseUsername(link, out _) || YoutubeHelpers.TryParseHandle(link, out _))
            return LinkKind.Channel;

        if (IsYoutubeUrl(link) && YoutubeHelpers.TryParseVideoId(link, out _))
            return LinkKind.Video;

        return LinkKind.None;
    }

    private static bool IsYoutubeUrl(string link) =>
        link.Contains("youtube.", StringComparison.OrdinalIgnoreCase) || link.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

    public static async Task<ResolvedSource> ResolveAsync(string link, CancellationToken token)
    {
        var client = GlobalConsts.YoutubeClient;

        if (YoutubeHelpers.TryParsePlaylistId(link, out var playlistId))
        {
            var playlist = await client.Playlists.GetAsync(playlistId.Value, token).ConfigureAwait(false);
            var videos = await client.Playlists.GetVideosAsync(playlist.Id, token).CollectAsync().ConfigureAwait(false);
            return new ResolvedSource
            {
                Kind = LinkKind.Playlist,
                Link = link,
                Title = playlist.Title,
                Author = playlist.Author?.ChannelTitle,
                ThumbnailUrl = Thumbnail(videos.FirstOrDefault()?.Id, "hqdefault"),
                Playlist = new FullPlaylist(playlist, videos),
                Videos = videos.Cast<IVideo>().ToList()
            };
        }

        Channel channel = null;
        if (YoutubeHelpers.TryParseChannelId(link, out var channelId))
            channel = await client.Channels.GetAsync(channelId, token).ConfigureAwait(false);
        else if (YoutubeHelpers.TryParseUsername(link, out var username))
            channel = await client.Channels.GetByUserAsync(username, token).ConfigureAwait(false);
        else if (YoutubeHelpers.TryParseHandle(link, out var handle))
            channel = await client.Channels.GetByHandleAsync(handle, token).ConfigureAwait(false);

        if (channel != null)
        {
            var uploads = await client.Channels.GetUploadsAsync(channel.Id, token).CollectAsync().ConfigureAwait(false);
            return new ResolvedSource
            {
                Kind = LinkKind.Channel,
                Link = link,
                Title = channel.Title,
                Author = channel.Title,
                ThumbnailUrl = channel.Thumbnails.OrderByDescending(x => x.Resolution.Area).FirstOrDefault()?.Url,
                Playlist = new FullPlaylist(null, null, channel.Title),
                Videos = uploads.Cast<IVideo>().ToList()
            };
        }

        if (YoutubeHelpers.TryParseVideoId(link, out var videoId))
        {
            var video = await client.Videos.GetAsync(videoId, token).ConfigureAwait(false);
            return new ResolvedSource
            {
                Kind = LinkKind.Video,
                Link = link,
                Title = video.Title,
                Author = video.Author.ChannelTitle,
                ThumbnailUrl = Thumbnail(video.Id, "hqdefault"),
                Playlist = new FullPlaylist(null, null),
                Videos = [video]
            };
        }

        throw new InvalidOperationException(Loc.Get("NoVideosToDownload"));
    }

    public static string Thumbnail(string videoId, string size = "mqdefault") =>
        string.IsNullOrEmpty(videoId) ? null : $"https://i.ytimg.com/vi/{videoId}/{size}.jpg";
}
