using Microsoft.Web.WebView2.Core;

namespace YoutubePlaylistDownloader.Views;

/// <summary>
/// Google's own sign-in page in an embedded Edge (WebView2). Once the session reaches youtube.com signed in,
/// its cookies are stored for YoutubeExplode and the window closes by itself.
/// </summary>
public partial class SignInWindow : Window
{
    private const string StartUrl = "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";

    private bool finished;

    /// <summary>The WebView2 runtime isn't installed, so the window couldn't show anything.</summary>
    public bool RuntimeMissing { get; private set; }

    public int CookieCount { get; private set; }

    public SignInWindow()
    {
        InitializeComponent();
        SourceInitialized += (s, e) => WindowFrame.Apply(this);
        Loaded += async (s, e) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: YoutubeCookies.BrowserProfilePath);
            await Browser.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            RuntimeMissing = true;
            Close();
            return;
        }

        var core = Browser.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        // Keep "open in new window" links (account chooser, help pages) inside this window.
        core.NewWindowRequested += (s, e) =>
        {
            e.Handled = true;
            core.Navigate(e.Uri);
        };

        core.NavigationCompleted += async (s, e) =>
        {
            Loading.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
            await TryCaptureAsync();
        };

        core.Navigate(StartUrl);
    }

    private async Task TryCaptureAsync()
    {
        if (finished || !Uri.TryCreate(Browser.CoreWebView2.Source, UriKind.Absolute, out var uri)
            || !uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
            return;

        var manager = Browser.CoreWebView2.CookieManager;
        var cookies = (await manager.GetCookiesAsync("https://www.youtube.com"))
            .Concat(await manager.GetCookiesAsync("https://accounts.google.com"))
            .ToList();

        // SAPISID only exists for a signed-in session; YoutubeExplode signs its requests with it.
        if (finished || !cookies.Any(c => c.Name is "SAPISID" or "__Secure-3PAPISID"))
            return;

        finished = true;
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

        CookieCount = YoutubeCookies.Save(converted);
        DialogResult = CookieCount > 0;
    }
}
