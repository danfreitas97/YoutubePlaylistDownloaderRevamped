using Xunit;
using YoutubePlaylistDownloader.Services;

namespace YoutubePlaylistDownloader.Tests;

public sealed class LiveDownloadFactAttribute : FactAttribute
{
    public LiveDownloadFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("YPDR_LIVE_TESTS") != "1")
            Skip = "Set YPDR_LIVE_TESTS=1 to test a real YouTube download (requires network access).";
    }
}

public class LiveDownloadTests
{
    [LiveDownloadFact]
    [Trait("Category", "Integration")]
    public Task SingleVideoDownloadsOriginalAudio() => DownloadSingleVideo(audioOnly: true);

    [LiveDownloadFact]
    [Trait("Category", "Integration")]
    public Task SingleVideoDownloadsMp4() => DownloadSingleVideo(audioOnly: false);

    private static async Task DownloadSingleVideo(bool audioOnly)
    {
        if (!audioOnly)
            Assert.True(GlobalConsts.FFmpegExists, "Install FFmpeg before running the MP4 integration test.");
        GlobalConsts.ConversionsLocker = new SemaphoreSlim(1, 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var source = await LinkResolver.ResolveAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw", timeout.Token);
        Assert.Equal(LinkKind.Video, source.Kind);
        var video = Assert.Single(source.Videos);
        var directory = Path.Combine(Path.GetTempPath(), "YPDR-tests", Guid.NewGuid().ToString("N"));
        var settings = GlobalConsts.DefaultDownloadSettings();
        settings.AudioOnly = audioOnly;
        settings.Convert = false;
        settings.TagAudioFile = false;
        var item = new DownloadItem(video, 0, source.Playlist, settings, directory);
        using var cancellation = timeout.Token.Register(item.Cancel);
        try
        {
            await item.RunAsync();
            Assert.True(item.Status == DownloadStatus.Done, $"{item.Status}: {item.ErrorMessage}");
            Assert.True(new FileInfo(item.OutputPath).Length > 0);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
