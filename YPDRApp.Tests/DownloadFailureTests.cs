using System.Net;
using System.Net.Http;
using Xunit;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YoutubePlaylistDownloader.Services;

namespace YoutubePlaylistDownloader.Tests;

public class DownloadFailureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "YPDR-tests", Guid.NewGuid().ToString("N"));

    private DownloadItem CreateItem()
    {
        var video = new Video("jNQXAC9IVRw", "Ten hours", new Author("UC3xnGqlcL3y-GXz5N3wiTJQ", "Test"),
            DateTimeOffset.UtcNow, "", TimeSpan.FromHours(10), [], [], new Engagement(1, 1, 0));
        return new DownloadItem(video, 0, null, GlobalConsts.DefaultDownloadSettings(), directory);
    }

    [Fact]
    public async Task NetworkTimeoutBecomesRetryableErrorAndCleansDestinationWorkspace()
    {
        var item = CreateItem();
        using var http = new HttpClient(new Handler((_, _) =>
        {
            Assert.Single(Directory.GetDirectories(directory, ".ypdr-*"));
            throw new TaskCanceledException("Simulated network timeout");
        }));
        await item.RunAsync(new YoutubeClient(http));
        Assert.Equal(DownloadStatus.Error, item.Status);
        Assert.Equal("DownloadTimedOut", item.ErrorMessage);
        Assert.Empty(Directory.GetFileSystemEntries(directory));
        item.Retry();
        Assert.Equal(DownloadStatus.Queued, item.Status);
    }

    [Fact]
    public async Task UserPauseIsNotReportedAsNetworkFailure()
    {
        var item = CreateItem();
        using var http = new HttpClient(new Handler((_, token) =>
        {
            item.Pause();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Expected cancellation");
        }));
        await item.RunAsync(new YoutubeClient(http));
        Assert.Equal(DownloadStatus.Paused, item.Status);
        Assert.Null(item.ErrorMessage);
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    [Fact]
    public async Task InvalidDestinationNeverReportsSuccess()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        await File.WriteAllTextAsync(directory, "This is a file, not a directory");
        using var http = new HttpClient(new Handler((_, _) => throw new Exception("Network must not be used")));
        var item = CreateItem();
        await item.RunAsync(new YoutubeClient(http));
        Assert.Equal(DownloadStatus.Error, item.Status);
        Assert.Null(item.OutputPath);
        Assert.False(item.IsFinished);
        File.Delete(directory);
    }

    [Fact]
    public async Task ConcurrentDownloadsWithSameTitlePreserveEveryFile()
    {
        Directory.CreateDirectory(directory);
        var tasks = Enumerable.Range(0, 12).Select(async i =>
        {
            var source = Path.Combine(directory, $"source-{i}");
            await File.WriteAllTextAsync(source, i.ToString());
            return await Task.Run(() => DownloadItem.MoveToUniquePath(source, directory, "Same title", "mp4"));
        });
        var destinations = await Task.WhenAll(tasks);
        Assert.Equal(12, destinations.Distinct().Count());
        Assert.Equal(12, destinations.Select(File.ReadAllText).Distinct().Count());
    }

    [Fact]
    public void MuxedOnlyManifestCanDownloadVideoAndConvertAudio()
    {
        var stream = new MuxedStreamInfo("https://example.org/media", Container.Mp4,
            new FileSize(5L * 1024 * 1024 * 1024), new Bitrate(1000000), "mp4a.40.2", null, true,
            "avc1.42001E", new VideoQuality("360p", 360, 30), new Resolution(640, 360));
        var manifest = new StreamManifest([stream]);
        var item = CreateItem();
        Assert.Same(stream, item.SelectVideoStream(manifest, "mp4"));
        Assert.Same(stream, item.SelectAudioStream(manifest));
        item.Settings.Convert = true;
        Assert.Same(stream, item.SelectAudioStream(manifest));
        item.Settings.AudioOnly = true;
        Assert.Null(item.SelectAudioStream(manifest));
        Assert.Null(item.SelectVideoStream(new StreamManifest([]), "mp4"));
        Assert.Null(item.SelectAudioStream(new StreamManifest([])));
    }

    [Fact]
    public void ForbiddenResponseIsNotMisreportedAsNoInternet() =>
        Assert.Equal("YoutubeForbidden", GlobalConsts.FriendlyError(new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden)));

    [Theory]
    [InlineData("CON")]
    [InlineData("nul.mp4")]
    [InlineData("LPT1")]
    [InlineData("...")]
    [InlineData("A title. ")]
    public void ReservedAndTrailingDotNamesCanBeSaved(string title)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, GlobalConsts.CleanFileName(title) + ".mp4");
        File.WriteAllText(path, "download");
        Assert.Equal("download", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        else if (File.Exists(directory)) File.Delete(directory);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
