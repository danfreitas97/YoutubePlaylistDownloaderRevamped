namespace YoutubePlaylistDownloader.Services;

public record UpdateInfo(Version Version, string Changelog);

/// <summary>
/// Checks the repository for a newer version and downloads its setup.
/// </summary>
public static class UpdateService
{
    public static string SetupPath { get; private set; }
    public static bool RunSetupOnExit { get; set; }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return client;
    }

    /// <summary>Returns the newer version, or null when this one is the latest.</summary>
    public static async Task<UpdateInfo> CheckAsync()
    {
        using var client = CreateClient();
        var latest = Version.Parse((await client.GetStringAsync($"{GlobalConsts.RepositoryRawUrl}latestVersionWithRevision.txt").ConfigureAwait(false)).Trim());

        if (latest <= GlobalConsts.VERSION)
            return null;

        var changelog = await client.GetStringAsync($"{GlobalConsts.RepositoryRawUrl}changelog.txt").ConfigureAwait(false);
        return new UpdateInfo(latest, changelog);
    }

    public static async Task DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken token)
    {
        using var client = CreateClient();
        var link = (await client.GetStringAsync($"{GlobalConsts.RepositoryRawUrl}latestVersionLink.txt", token).ConfigureAwait(false)).Trim();
        var path = Path.Combine(GlobalConsts.TempFolderPath, $"Setup {update.Version}.exe");

        using var response = await client.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        await using (var output = File.Create(path))
        {
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                read += count;
                if (total > 0)
                    progress?.Report(read * 100.0 / total);
            }
        }

        SetupPath = path;
    }

    public static void RunSetup()
    {
        if (!string.IsNullOrWhiteSpace(SetupPath) && File.Exists(SetupPath))
            Process.Start(new ProcessStartInfo(SetupPath) { UseShellExecute = true });
    }
}
