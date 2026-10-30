using Xunit;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubePlaylistDownloader.Services;

namespace YoutubePlaylistDownloader.Tests;

public class DownloadRegressionTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw")]
    [InlineData("www.youtube.com/watch?v=jNQXAC9IVRw")]
    [InlineData("youtu.be/jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&list=PL1234567890&index=2")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&list=RDjNQXAC9IVRw&start_radio=1")]
    [InlineData("https://youtu.be/jNQXAC9IVRw?si=share")]
    [InlineData("https://youtu.be/jNQXAC9IVRw/?list=PL1234567890")]
    [InlineData("https://m.youtube.com/watch?v=jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/shorts/jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/live/jNQXAC9IVRw")]
    public void VideoLinksSelectOnlyTheVideo(string link) =>
        Assert.Equal(LinkKind.Video, LinkResolver.Detect(link));

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PL1234567890", LinkKind.Playlist)]
    [InlineData("https://www.youtube.com/channel/UC3xnGqlcL3y-GXz5N3wiTJQ", LinkKind.Channel)]
    [InlineData("https://www.youtube.com/@YouTube", LinkKind.Channel)]
    [InlineData("https://www.youtube.com/user/YouTube", LinkKind.Channel)]
    public void CollectionLinksStillLoadCollections(string link, LinkKind expected) =>
        Assert.Equal(expected, LinkResolver.Detect(link));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a link")]
    [InlineData("https://notyoutube.com/watch?v=jNQXAC9IVRw")]
    [InlineData("https://youtube.com.example.org/watch?v=jNQXAC9IVRw")]
    [InlineData("https://example.org/youtube.com/playlist?list=PL1234567890")]
    public void InvalidLinksAreRejected(string link) =>
        Assert.Equal(LinkKind.None, LinkResolver.Detect(link));

    [Fact]
    public void SingleVideoWithoutPlaylistBuildsFilenameAndIsSelected()
    {
        var video = new Video("jNQXAC9IVRw", "Me at the zoo",
            new Author("UC3xnGqlcL3y-GXz5N3wiTJQ", "jawed"),
            DateTimeOffset.UtcNow, "", TimeSpan.FromSeconds(19), [], [], new Engagement(1, 1, 0));
        var settings = GlobalConsts.DefaultDownloadSettings();
        settings.FilenamePattern = "$title$playlist";
        Assert.Equal("Me at the zoo", settings.GetFilenameByPattern(video, 0, video.Title));
        var item = new YoutubePlaylistDownloader.ViewModels.VideoItemViewModel(video, 0, null, () => { });
        Assert.True(item.IsSelected);
    }
}
