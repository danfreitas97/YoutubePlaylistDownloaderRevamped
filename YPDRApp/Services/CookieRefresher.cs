using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace YoutubePlaylistDownloader.Services;

public enum CookieRefreshResult { Refreshed, NotSignedIn, Imported, SignedOut, Failed, Superseded }

/// <summary>
/// Keeps the stored cookies current. Google rotates the session cookies, so a copy taken once eventually stops working;
/// the sign-in window's browser profile still holds the session, and visiting YouTube with it yields fresh cookies.
/// </summary>
public static class CookieRefresher
{
    private const string RefreshUrl = "https://www.youtube.com/";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(60);

    private static DispatcherTimer timer;
    private static Task<CookieRefreshResult> current;

    /// <summary>Refreshes now and then periodically while the app is open. Call on the UI thread once the main window is shown.</summary>
    public static void Start()
    {
        timer ??= new DispatcherTimer(Interval, DispatcherPriority.Background, async (s, e) => await RefreshAsync(), Application.Current.Dispatcher);
        _ = RefreshAsync();
    }

    /// <summary>Refreshes the stored cookies from the sign-in browser; joins a refresh already running. Call on the UI thread.</summary>
    public static Task<CookieRefreshResult> RefreshAsync()
    {
        if (current is { IsCompleted: false })
            return current;

        return current = RunAsync();
    }

    /// <summary>
    /// The signed-in session's YouTube and Google cookies, or null when the browser isn't signed in.
    /// </summary>
    internal static async Task<List<System.Net.Cookie>> ReadSignedInAsync(CoreWebView2CookieManager manager)
    {
        var cookies = (await manager.GetCookiesAsync("https://www.youtube.com"))
            .Concat(await manager.GetCookiesAsync("https://accounts.google.com"))
            .ToList();

        // SAPISID only exists for a signed-in session; YoutubeExplode signs its requests with it.
        if (!cookies.Any(c => c.Name is "SAPISID" or "__Secure-3PAPISID"))
            return null;

        var converted = new List<System.Net.Cookie>();
        foreach (var cookie in cookies)
        {
            try
            {
                converted.Add(cookie.ToSystemNetCookie());
            }
            catch (System.Net.CookieException)
            {
                // Skip cookies with characters System.Net.Cookie doesn't accept.
            }
        }

        return converted;
    }

    private static async Task<CookieRefreshResult> RunAsync()
    {
        if (!YoutubeCookies.HasCookies)
            return CookieRefreshResult.NotSignedIn;

        // An existing profile may belong to a different account than imported or legacy cookies.
        if (!YoutubeCookies.CanRefresh)
            return CookieRefreshResult.Imported;

        var sessionVersion = YoutubeCookies.SessionVersion;

        var owner = Application.Current?.MainWindow;
        if (owner == null)
            return CookieRefreshResult.Failed;

        CoreWebView2Controller controller = null;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: YoutubeCookies.BrowserProfilePath);
            if (sessionVersion != YoutubeCookies.SessionVersion)
                return CookieRefreshResult.Superseded;
            controller = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(owner).Handle);
            if (sessionVersion != YoutubeCookies.SessionVersion)
                return CookieRefreshResult.Superseded;
            controller.IsVisible = false;

            var loaded = new TaskCompletionSource<bool>();
            controller.CoreWebView2.NavigationCompleted += (s, e) => loaded.TrySetResult(e.IsSuccess);
            controller.CoreWebView2.Navigate(RefreshUrl);

            if (await Task.WhenAny(loaded.Task, Task.Delay(PageTimeout)) != loaded.Task || !loaded.Task.Result)
                return CookieRefreshResult.Failed;

            var cookies = await ReadSignedInAsync(controller.CoreWebView2.CookieManager);
            if (sessionVersion != YoutubeCookies.SessionVersion)
                return CookieRefreshResult.Superseded;
            if (cookies == null)
            {
                // Keep existing cookies; report that the browser requires an explicit sign-in.
                await GlobalConsts.Log("The sign-in session is no longer signed in; cookies were not refreshed", "CookieRefresher");
                return CookieRefreshResult.SignedOut;
            }

            return YoutubeCookies.TryRefresh(sessionVersion, cookies) ? CookieRefreshResult.Refreshed : CookieRefreshResult.Failed;
        }
        catch (Exception ex)
        {
            await GlobalConsts.Log(ex.ToString(), "CookieRefresher");
            return CookieRefreshResult.Failed;
        }
        finally
        {
            controller?.Close();
        }
    }
}
