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
    private bool closed;

    /// <summary>The WebView2 runtime isn't installed, so the window couldn't show anything.</summary>
    public bool RuntimeMissing { get; private set; }

    public int CookieCount { get; private set; }

    public SignInWindow()
    {
        InitializeComponent();
        SourceInitialized += (s, e) => WindowFrame.Apply(this);
        Loaded += async (s, e) => await StartAsync();

        // Closing the window alone leaves the browser processes running, holding the profile folder open.
        Closed += (s, e) =>
        {
            closed = true;
            Browser.Dispose();
        };
    }

    private async Task StartAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: YoutubeCookies.BrowserProfilePath);
            if (closed)
                return;
            await Browser.EnsureCoreWebView2Async(environment);
        }
        catch (Exception) when (closed)
        {
            // Closing disposes the browser and may abort initialization already in progress.
            return;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            RuntimeMissing = true;
            Close();
            return;
        }

        if (closed)
            return;

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
            if (closed)
                return;
            Loading.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
            await TryCaptureAsync();
        };

        core.Navigate(StartUrl);
    }

    private async Task TryCaptureAsync()
    {
        if (closed || finished || !Uri.TryCreate(Browser.CoreWebView2.Source, UriKind.Absolute, out var uri)
            || !uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
            return;

        List<System.Net.Cookie> cookies;
        try
        {
            cookies = await CookieRefresher.ReadSignedInAsync(Browser.CoreWebView2.CookieManager);
        }
        catch (Exception) when (closed)
        {
            // Cookie reads may finish after the window and browser were disposed.
            return;
        }
        if (closed || finished || cookies == null)
            return;

        finished = true;
        CookieCount = YoutubeCookies.Save(cookies);
        DialogResult = CookieCount > 0;
    }
}
