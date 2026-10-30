using System.Net;

namespace YoutubePlaylistDownloader.Services;

/// <summary>
/// Cookies of a signed-in YouTube session, captured by the sign-in window or imported from a browser's cookies.txt (Netscape format).
/// YouTube rate-limits anonymous requests; sending a logged-in session's cookies avoids that.
/// </summary>
public static class YoutubeCookies
{
    private static readonly string FilePath = Path.Combine(GlobalConsts.AppDataPath, "cookies.txt");
    private static IReadOnlyList<Cookie> cookies;

    /// <summary>Browser profile of the sign-in window, kept apart from the user's own browsers.</summary>
    public static string BrowserProfilePath { get; } = Path.Combine(GlobalConsts.AppDataPath, "SignIn");

    /// <summary>Raised after signing in, importing cookies or signing out.</summary>
    public static event EventHandler Changed;

    public static IReadOnlyList<Cookie> Current => cookies ??= File.Exists(FilePath) ? Parse(File.ReadAllLines(FilePath)) : [];

    public static bool HasCookies => Current.Count > 0;

    /// <summary>Parses and stores the file; returns how many YouTube/Google cookies it had.</summary>
    public static int Import(string path)
    {
        var lines = File.ReadAllLines(path);
        var parsed = Parse(lines);
        if (parsed.Count == 0)
            return 0;

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        File.WriteAllLines(FilePath, lines);
        cookies = parsed;
        Changed?.Invoke(null, EventArgs.Empty);
        return parsed.Count;
    }

    /// <summary>Stores cookies captured by the sign-in window; returns how many YouTube/Google cookies were kept.</summary>
    public static int Save(IEnumerable<Cookie> captured)
    {
        var lines = new List<string> { "# Netscape HTTP Cookie File" };
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
        Changed?.Invoke(null, EventArgs.Empty);
        return parsed.Count;
    }

    public static void Clear()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);

        // Forget the sign-in window's session too, so the next sign-in can pick another account.
        try
        {
            if (Directory.Exists(BrowserProfilePath))
                Directory.Delete(BrowserProfilePath, recursive: true);
        }
        catch (IOException)
        {
            // Still locked by a closing browser process; it is overwritten by the next sign-in anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }

        cookies = [];
        Changed?.Invoke(null, EventArgs.Empty);
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
