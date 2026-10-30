using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace YoutubePlaylistDownloader.Services;

/// <summary>A playlist in the signed-in account's library.</summary>
public class LibraryPlaylist
{
    public string Id { get; init; }
    public string Title { get; init; }
    public string ThumbnailUrl { get; init; }

    /// <summary>Number of videos, when YouTube shows it.</summary>
    public int? Count { get; init; }

    public string CountText => Count is int count ? count == 1 ? Loc.Get("OneVideo") : Loc.Format("VideosCount", count) : null;
    public bool IsLikedMusic => Id == YoutubeLibrary.LikedMusicId;
    public string Link => $"https://www.youtube.com/playlist?list={Id}";
}

/// <summary>
/// Lists the playlists of the signed-in account (its own, the automatic ones and the ones it saved), which YoutubeExplode
/// can't do, by calling YouTube's internal browse API with the session cookies. Names come back in the account's language.
/// </summary>
public static class YoutubeLibrary
{
    public const string LikedMusicId = "LM";

    private const string Origin = "https://www.youtube.com";
    private const string BrowseUrl = "https://www.youtube.com/youtubei/v1/browse?prettyPrint=false";
    private const string ClientVersion = "2.20251020.00.00";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    // Pages of the grid are fetched until YouTube stops sending a continuation, with a cap against loops.
    private const int MaxPages = 20;

    private static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false });

    /// <summary>Liked music first, then every playlist of the library page in YouTube's own order.</summary>
    public static async Task<IReadOnlyList<LibraryPlaylist>> GetPlaylistsAsync(CancellationToken token)
    {
        var likedMusic = GetLikedMusicAsync(token);

        var found = new List<LibraryPlaylist>();
        var response = await BrowseAsync(new JObject { ["browseId"] = "FEplaylist_aggregation" }, token).ConfigureAwait(false);

        for (var page = 0; page < MaxPages; page++)
        {
            found.AddRange(response.SelectTokens("$..lockupViewModel").Select(ParseLockup).Where(x => x != null));

            var continuation = response.SelectTokens("$..continuationItemRenderer..continuationCommand.token").FirstOrDefault()?.Value<string>();
            if (string.IsNullOrEmpty(continuation))
                break;

            response = await BrowseAsync(new JObject { ["continuation"] = continuation }, token).ConfigureAwait(false);
        }

        var result = found.DistinctBy(x => x.Id).ToList();
        if (await likedMusic.ConfigureAwait(false) is { } liked)
            result.Insert(0, liked);

        return result;
    }

    /// <summary>
    /// Liked music isn't in the library grid: it belongs to YouTube Music, but the same session can read it.
    /// Null when the account has none or YouTube doesn't answer for it.
    /// </summary>
    private static async Task<LibraryPlaylist> GetLikedMusicAsync(CancellationToken token)
    {
        try
        {
            var response = await BrowseAsync(new JObject { ["browseId"] = "VL" + LikedMusicId }, token).ConfigureAwait(false);
            var title = response.SelectToken("$.metadata.playlistMetadataRenderer.title")?.Value<string>();
            return string.IsNullOrWhiteSpace(title) ? null : new LibraryPlaylist { Id = LikedMusicId, Title = title };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await GlobalConsts.Log(ex.ToString(), "GetLikedMusic").ConfigureAwait(false);
            return null;
        }
    }

    private static LibraryPlaylist ParseLockup(JToken lockup)
    {
        var id = lockup.Value<string>("contentId");
        if (lockup.Value<string>("contentType") != "LOCKUP_CONTENT_TYPE_PLAYLIST" || string.IsNullOrEmpty(id))
            return null;

        // The badge reads like "28 videos" in the account's language; only the digits matter.
        var badge = lockup.SelectTokens("$..thumbnailBadgeViewModel.text").FirstOrDefault()?.Value<string>();
        var digits = new string((badge ?? string.Empty).Where(char.IsDigit).ToArray());

        return new LibraryPlaylist
        {
            Id = id,
            Title = lockup.SelectToken("$.metadata.lockupMetadataViewModel.title.content")?.Value<string>() ?? id,
            ThumbnailUrl = lockup.SelectTokens("$.contentImage..image.sources[*].url").FirstOrDefault()?.Value<string>(),
            Count = int.TryParse(digits, out var count) ? count : null
        };
    }

    private static async Task<JObject> BrowseAsync(JObject body, CancellationToken token)
    {
        var cookies = YoutubeCookies.Current.Where(x => x.Domain.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)).ToList();

        // A signed-in session has SAPISID; requests are authorized with a hash of it, as youtube.com does.
        var sapisid = cookies.FirstOrDefault(x => x.Name == "SAPISID")?.Value ?? cookies.FirstOrDefault(x => x.Name == "__Secure-3PAPISID")?.Value
            ?? throw new InvalidOperationException("Not signed in.");

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{timestamp} {sapisid} {Origin}"))).ToLowerInvariant();

        // No "hl": YouTube then answers in the account's own language.
        body["context"] = new JObject { ["client"] = new JObject { ["clientName"] = "WEB", ["clientVersion"] = ClientVersion } };

        using var request = new HttpRequestMessage(HttpMethod.Post, BrowseUrl)
        {
            Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"SAPISIDHASH {timestamp}_{hash}");
        request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.DistinctBy(x => x.Name).Select(x => $"{x.Name}={x.Value}")));
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        request.Headers.TryAddWithoutValidation("X-Origin", Origin);
        request.Headers.TryAddWithoutValidation("X-Goog-AuthUser", "0");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = JObject.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));

        // An expired session still answers 200, but as a signed-out visitor.
        if (json.SelectTokens("$.responseContext..loggedOut").Any(x => x.Type == JTokenType.Boolean && x.Value<bool>()))
            throw new InvalidOperationException("The YouTube session has expired.");

        return json;
    }
}
