using System.Net;

namespace YoutubePlaylistDownloader.Services;

/// <summary>
/// Cookies of a signed-in YouTube session, captured by the sign-in window or imported from a browser's cookies.txt (Netscape format).
/// YouTube rate-limits anonymous requests; sending a logged-in session's cookies avoids that.
/// </summary>
public static class YoutubeCookies
{
    private static readonly YoutubeCookieStore store = new(GlobalConsts.AppDataPath);

    public static string BrowserProfilePath => store.BrowserProfilePath;
    public static IReadOnlyList<Cookie> Current => store.Current;
    public static bool HasCookies => store.HasCookies;
    public static event EventHandler Changed
    {
        add => store.Changed += value;
        remove => store.Changed -= value;
    }

    public static int Import(string path) => store.Import(path);
    public static int Save(IEnumerable<Cookie> captured) => store.Save(captured);
    public static void Clear() => store.Clear();
    public static void DeletePendingProfile() => store.DeletePendingProfile();
    internal static long SessionVersion => store.SessionVersion;
    internal static bool CanRefresh => store.CanRefresh;
    internal static bool TryRefresh(long version, IEnumerable<Cookie> captured) => store.TryRefresh(version, captured);
}

// Mutations run on the UI thread. The lock also protects lazy loading against download threads reading Current.
internal sealed class YoutubeCookieStore
{
    private const string BrowserSessionHeader = "# YPDR source: sign-in browser";
    private readonly object gate = new();
    private readonly string FilePath;
    private IReadOnlyList<Cookie> cookies;
    private bool browserSession;
    private long sessionVersion;

    internal YoutubeCookieStore(string directory)
    {
        FilePath = Path.Combine(directory, "cookies.txt");
        BrowserProfilePath = Path.Combine(directory, "SignIn");
        PendingDeletePath = Path.Combine(directory, "SignIn.delete");
    }

    /// <summary>Browser profile of the sign-in window, kept apart from the user's own browsers.</summary>
    public string BrowserProfilePath { get; }

    // Left behind when signing out couldn't delete the browser profile because a browser process still had it open.
    private readonly string PendingDeletePath;

    /// <summary>Raised after signing in, importing cookies or signing out.</summary>
    public event EventHandler Changed;

    public IReadOnlyList<Cookie> Current
    {
        get
        {
            lock (gate)
            {
                if (cookies == null)
                {
                    var lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : [];
                    browserSession = lines.Contains(BrowserSessionHeader);
                    cookies = Parse(lines);
                }
                return cookies;
            }
        }
    }

    public bool HasCookies => Current.Count > 0;
    internal long SessionVersion => sessionVersion;
    internal bool CanRefresh => HasCookies && browserSession && Directory.Exists(BrowserProfilePath);

    /// <summary>Parses and stores the file; returns how many YouTube/Google cookies it had.</summary>
    public int Import(string path)
    {
        // Even a file exported by this app is an import, not proof of a matching browser session.
        var lines = File.ReadAllLines(path).Where(line => line.Trim() != BrowserSessionHeader).ToArray();
        var parsed = Parse(lines);
        if (parsed.Count == 0)
            return 0;

        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, lines);
            cookies = parsed;
            browserSession = false;
            sessionVersion++;
        }
        Changed?.Invoke(null, EventArgs.Empty);
        return parsed.Count;
    }

    /// <summary>
    /// Stores cookies captured from the sign-in browser; returns how many YouTube/Google cookies were kept.
    /// </summary>
    public int Save(IEnumerable<Cookie> captured)
    {
        int count;
        lock (gate)
        {
            count = SaveCore(captured);
            if (count == 0)
                return 0;
            sessionVersion++;
            // Only a new explicit sign-in may cancel pending profile deletion.
            File.Delete(PendingDeletePath);
        }
        Changed?.Invoke(null, EventArgs.Empty);
        return count;
    }

    internal bool TryRefresh(long version, IEnumerable<Cookie> captured)
    {
        lock (gate)
            return version == sessionVersion && CanRefresh && SaveCore(captured) > 0;
    }

    private int SaveCore(IEnumerable<Cookie> captured)
    {
        var lines = new List<string> { "# Netscape HTTP Cookie File", BrowserSessionHeader };
        foreach (var cookie in captured)
        {
            var expires = cookie.Expires == DateTime.MinValue ? 0 : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds();
            var subdomains = cookie.Domain.StartsWith('.') ? "TRUE" : "FALSE";
            var secure = cookie.Secure ? "TRUE" : "FALSE";
            lines.Add($"{(cookie.HttpOnly ? "#HttpOnly_" : "")}{cookie.Domain}\t{subdomains}\t{cookie.Path}\t{secure}\t{expires}\t{cookie.Name}\t{cookie.Value}");
        }

        var parsed = Parse(lines);
        if (parsed.Count == 0)
            return 0;

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        File.WriteAllLines(FilePath, lines);
        cookies = parsed;
        browserSession = true;
        return parsed.Count;
    }

    public void Clear()
    {
        lock (gate)
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);

            // Invalidate pending refreshes before attempting browser profile cleanup.
            cookies = [];
            browserSession = false;
            sessionVersion++;

            // Still open in a browser process: finish at the next start.
            if (!DeleteBrowserProfile())
                File.WriteAllText(PendingDeletePath, string.Empty);
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Finishes a sign-out whose browser profile was in use. Call at startup, before the profile is opened.</summary>
    public void DeletePendingProfile()
    {
        if (File.Exists(PendingDeletePath) && DeleteBrowserProfile())
            File.Delete(PendingDeletePath);
    }

    /// <summary>
    /// Deletes the browser profile entirely or not at all. A recursive delete stops at the first file a browser
    /// process holds and leaves half a profile behind; renaming the folder first fails before anything is removed.
    /// </summary>
    private bool DeleteBrowserProfile()
    {
        if (!Directory.Exists(BrowserProfilePath))
            return true;

        var doomed = BrowserProfilePath + ".deleting";
        try
        {
            if (Directory.Exists(doomed))
                Directory.Delete(doomed, recursive: true);
            Directory.Move(BrowserProfilePath, doomed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        try
        {
            Directory.Delete(doomed, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Out of the way already; the next sign-out clears what is left.
        }

        return true;
    }

    private static List<Cookie> Parse(IEnumerable<string> lines)
    {
        var result = new List<Cookie>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("#HttpOnly_", StringComparison.Ordinal))
                line = line["#HttpOnly_".Length..];
            else if (line.Length == 0 || line.StartsWith('#'))
                continue;

            // domain, include subdomains, path, secure, expiry, name, value
            var parts = line.Split('\t');
            if (parts.Length < 7)
                continue;

            var domain = parts[0];
            if (!domain.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) && !domain.Contains("google.com", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                result.Add(new Cookie(parts[5], parts[6], parts[2], domain) { Secure = parts[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase) });
            }
            catch (CookieException)
            {
                // Skip cookies with characters System.Net.Cookie doesn't accept.
            }
        }

        return result;
    }
}
