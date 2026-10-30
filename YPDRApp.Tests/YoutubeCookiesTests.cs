using System.Net;
using Xunit;
using YoutubePlaylistDownloader.Services;

namespace YoutubePlaylistDownloader.Tests;

public sealed class YoutubeCookiesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "YPDR-tests", Guid.NewGuid().ToString("N"));
    private readonly YoutubeCookieStore store;

    public YoutubeCookiesTests()
    {
        store = new YoutubeCookieStore(directory);
        Directory.CreateDirectory(store.BrowserProfilePath);
    }

    private static Cookie[] Session(string value) => [new Cookie("SAPISID", value, "/", ".youtube.com")];
    private string CookiePath => Path.Combine(directory, "cookies.txt");

    [Fact]
    public void RefreshAfterLogoutCannotRestoreCookiesOrClearPendingDeletion()
    {
        store.Save(Session("account-a"));
        var version = store.SessionVersion;
        store.Clear();
        // Model the browser still holding the old profile open at logout.
        Directory.CreateDirectory(store.BrowserProfilePath);
        var pendingDeletion = Path.Combine(directory, "SignIn.delete");
        File.WriteAllText(pendingDeletion, "");

        Assert.False(store.TryRefresh(version, Session("stale")));
        Assert.False(store.HasCookies);
        Assert.False(File.Exists(CookiePath));
        Assert.True(File.Exists(pendingDeletion));
    }

    [Fact]
    public void ImportInvalidatesPendingRefreshAndRemainsImportedAfterRestart()
    {
        store.Save(Session("account-a"));
        var version = store.SessionVersion;
        var imported = Path.Combine(directory, "import.txt");
        File.WriteAllText(imported, ".youtube.com\tTRUE\t/\tTRUE\t0\tSAPISID\taccount-b");
        store.Import(imported);

        Assert.False(store.TryRefresh(version, Session("account-a-refreshed")));
        Assert.Equal("account-b", Assert.Single(store.Current).Value);
        Assert.False(store.CanRefresh);
        var restarted = new YoutubeCookieStore(directory);
        Assert.Equal("account-b", Assert.Single(restarted.Current).Value);
        Assert.False(restarted.CanRefresh);
    }

    [Fact]
    public void NewSignInInvalidatesPreviousRefresh()
    {
        store.Save(Session("account-a"));
        var version = store.SessionVersion;
        store.Save(Session("account-b"));

        Assert.False(store.TryRefresh(version, Session("account-a-refreshed")));
        Assert.Equal("account-b", Assert.Single(store.Current).Value);
    }

    [Fact]
    public void BrowserSessionCanRefreshAfterRestartWithoutAccountChangeNotification()
    {
        store.Save(Session("account-a"));
        var restarted = new YoutubeCookieStore(directory);
        var notifications = 0;
        restarted.Changed += (_, _) => notifications++;

        Assert.True(restarted.CanRefresh);
        Assert.True(restarted.TryRefresh(restarted.SessionVersion, Session("renewed")));
        Assert.Equal("renewed", Assert.Single(new YoutubeCookieStore(directory).Current).Value);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void ImportingAppCookieFileDoesNotEnableBrowserRefresh()
    {
        store.Save(Session("account-a"));
        store.Import(CookiePath);

        Assert.True(store.HasCookies);
        Assert.False(store.CanRefresh);
        Assert.False(new YoutubeCookieStore(directory).CanRefresh);
    }

    [Fact]
    public void LegacyCookiesArePreservedButDoNotAssumeBrowserOwnership()
    {
        File.WriteAllText(CookiePath, "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\tSAPISID\tlegacy");

        Assert.True(store.HasCookies);
        Assert.Equal("legacy", Assert.Single(store.Current).Value);
        Assert.False(store.CanRefresh);
        Assert.False(store.TryRefresh(store.SessionVersion, Session("another-account")));
    }

    [Fact]
    public void EmptyRefreshDoesNotReplaceExistingSession()
    {
        store.Save(Session("account-a"));

        Assert.False(store.TryRefresh(store.SessionVersion, []));
        Assert.Equal("account-a", Assert.Single(store.Current).Value);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
