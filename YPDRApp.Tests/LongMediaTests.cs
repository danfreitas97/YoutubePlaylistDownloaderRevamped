using System.Diagnostics;
using System.Text;
using Xunit;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubePlaylistDownloader.Services;

namespace YoutubePlaylistDownloader.Tests;

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("YPDR_FFMPEG_TESTS") != "1")
            Skip = "Set YPDR_FFMPEG_TESTS=1 to run local FFmpeg tests.";
    }
}

public class LongMediaTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "YPDR-tests", Guid.NewGuid().ToString("N"));

    private DownloadItem CreateItem()
    {
        Assert.True(GlobalConsts.FFmpegExists);
        GlobalConsts.ConversionsLocker = new SemaphoreSlim(1, 1);
        Directory.CreateDirectory(directory);
        var video = new Video("jNQXAC9IVRw", "Ten hours", new Author("UC3xnGqlcL3y-GXz5N3wiTJQ", "Test"),
            DateTimeOffset.UtcNow, "", TimeSpan.FromHours(10), [], [], new Engagement(1, 1, 0));
        return new DownloadItem(video, 0, null, GlobalConsts.DefaultDownloadSettings(), directory);
    }

    [FfmpegFact]
    public async Task TenHourVideoCanBeMuxedAndMovedToDestination()
    {
        var item = CreateItem();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var source = Path.Combine(directory, "ten-hours.mkv");
        var output = Path.Combine(directory, "output.mp4");
        // 600 tiny frames span ten hours; tests duration handling without a multi-GB download.
        await item.RunFfmpegAsync($"-f lavfi -i color=s=16x16:r=1/60 -t 36000 -c:v libx264 -an -y \"{source}\"", timeout.Token);
        await item.RunFfmpegAsync($"-i \"{source}\" -map 0:v -c copy -y \"{output}\"", timeout.Token);
        var destination = DownloadItem.MoveToUniquePath(output, directory, "Ten hours", "mp4");
        Assert.True(new FileInfo(destination).Length > 0);
        using var probe = Process.Start(new ProcessStartInfo(GlobalConsts.FFmpegFilePath)
        {
            Arguments = $"-hide_banner -i \"{destination}\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        })!;
        var metadata = await probe.StandardError.ReadToEndAsync(timeout.Token);
        await probe.WaitForExitAsync(timeout.Token);
        Assert.Contains("Duration: 10:00:00", metadata);
    }

    [FfmpegFact]
    public async Task SmallWavRemainsStandardRiffWithLargeFileSupport()
    {
        var item = CreateItem();
        var output = Path.Combine(directory, "audio.wav");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await item.RunFfmpegAsync($"-f lavfi -i anullsrc=r=8000:cl=mono -t 1 {DownloadItem.AudioContainerOptions("wav")} -y \"{output}\"", timeout.Token);
        var header = (await File.ReadAllBytesAsync(output)).Take(64).ToArray();
        Assert.Equal("RIFF", Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(header, 8, 4));
        Assert.True(new FileInfo(output).Length >= 16000); // One second of 8 kHz, 16-bit mono audio.
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
