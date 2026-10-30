using System.IO.Compression;

namespace YoutubePlaylistDownloader.Services;

/// <summary>
/// Downloads ffmpeg on first use when it isn't next to the executable or on the PATH,
/// so a missing ffmpeg is a one-click fix instead of a dead end.
/// </summary>
public static class FfmpegService
{
    // Pinned build so a new upstream release can't change what users get.
    private const string DownloadUrl = "https://github.com/Tyrrrz/FFmpegBin/releases/download/9.0.2/ffmpeg-windows-{0}.zip";

    /// <summary>Approximate download size shown to the user, in MB.</summary>
    public const int DownloadSizeMb = 75;

    /// <summary>Per-user location, writable without admin rights.</summary>
    public static string InstallPath { get; } = Path.Combine(GlobalConsts.AppDataPath, "ffmpeg.exe");

    public static async Task DownloadAsync(IProgress<double> progress, CancellationToken token)
    {
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        var zipPath = Path.Combine(GlobalConsts.TempFolderPath, "ffmpeg.zip");
        Directory.CreateDirectory(GlobalConsts.TempFolderPath);

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(string.Format(DownloadUrl, architecture), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? 0;
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = File.Create(zipPath))
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

            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("ffmpeg.exe not found in the downloaded archive.");

            // Extract next to the target first so a half-written file never passes as ffmpeg.
            Directory.CreateDirectory(Path.GetDirectoryName(InstallPath));
            var partial = InstallPath + ".part";
            entry.ExtractToFile(partial, overwrite: true);
            File.Move(partial, InstallPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
        }
    }
}
