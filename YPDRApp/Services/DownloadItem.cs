using System.Globalization;

namespace YoutubePlaylistDownloader.Services;

public enum DownloadStatus { Queued, Downloading, Converting, Paused, Done, Skipped, Error }

/// <summary>
/// One video in the download queue: picks the streams, downloads them, runs ffmpeg and moves the result into place.
/// </summary>
public partial class DownloadItem : ObservableObject
{
    private static readonly string[] AudioFormats = ["mp3", "aac", "opus", "wav", "flac", "m4a", "ogg", "webm"];

    private CancellationTokenSource cts;
    private bool pauseRequested;
    private DownloadStatus statusBeforePause;

    public DownloadItem(IVideo video, int index, FullPlaylist playlist, DownloadSettings settings, string saveDirectory)
    {
        Video = video;
        Index = index;
        Playlist = playlist;
        Settings = settings;
        SaveDirectory = saveDirectory;
    }

    public IVideo Video { get; }

    /// <summary>Zero-based position of the video in its playlist; used by the file name pattern and the track tag.</summary>
    public int Index { get; }

    public FullPlaylist Playlist { get; }
    public DownloadSettings Settings { get; }
    public string SaveDirectory { get; }

    public string Title => Video.Title;
    public string Channel => Video.Author?.ChannelTitle;
    public string ThumbnailUrl => LinkResolver.Thumbnail(Video.Id);
    public string Duration => Video.Duration is TimeSpan d ? Format.Duration(d) : string.Empty;
    public bool IsAudio => Settings.AudioOnly || Settings.Convert;

    public string FormatLabel
    {
        get
        {
            if (Settings.AudioOnly)
                return Loc.Get("OriginalAudio");

            if (Settings.Convert)
                return Settings.SetBitrate ? $"{Settings.SaveFormat.ToUpperInvariant()} · {Settings.Bitrate} kbps" : Settings.SaveFormat.ToUpperInvariant();

            var quality = Settings.UseBestQuality ? Loc.Get("BestQuality") : Settings.Quality.Label;
            return $"{Settings.VideoSaveFormat.ToUpperInvariant()} · {quality}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsFinished), nameof(IsError), nameof(CanPause), nameof(CanResume), nameof(ShowBar), nameof(IsIndeterminate), nameof(Section), nameof(StatusText), nameof(DetailText))]
    private DownloadStatus status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    private string speedText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    private string etaText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    private string sizeText;

    [ObservableProperty]
    private string outputPath;

    public bool IsActive => Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Converting or DownloadStatus.Paused;
    public bool IsFinished => Status is DownloadStatus.Done or DownloadStatus.Skipped;
    public bool IsError => Status == DownloadStatus.Error;
    public bool CanPause => Status is DownloadStatus.Queued or DownloadStatus.Downloading;
    public bool CanResume => Status == DownloadStatus.Paused;
    public bool ShowBar => IsActive;
    public bool IsIndeterminate => Status == DownloadStatus.Converting && Progress <= 0;

    /// <summary>0 = in progress (including errors, so they stay in sight), 1 = finished.</summary>
    public int Section => IsFinished ? 1 : 0;

    public string StatusText => Status switch
    {
        DownloadStatus.Queued => Loc.Get("StatusQueued"),
        DownloadStatus.Downloading => $"{Math.Round(Progress)}%",
        DownloadStatus.Converting => Progress > 0 ? $"{Loc.Get("Converting")} {Math.Round(Progress)}%" : Loc.Get("Converting"),
        DownloadStatus.Paused => Loc.Format("StatusPaused", Math.Round(Progress)),
        DownloadStatus.Done => Loc.Get("StatusDone"),
        DownloadStatus.Skipped => Loc.Get("StatusSkipped"),
        DownloadStatus.Error => Loc.Format("StatusError", ErrorMessage),
        _ => string.Empty
    };

    public string DetailText => Status switch
    {
        DownloadStatus.Downloading when !string.IsNullOrEmpty(SpeedText) => string.IsNullOrEmpty(EtaText) ? SpeedText : $"{SpeedText} · {EtaText}",
        DownloadStatus.Done => SizeText,
        _ => string.Empty
    };

    /// <summary>Re-reads the localized texts after a language change.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(FormatLabel));
    }

    #region Control

    public void Pause()
    {
        if (!CanPause)
            return;

        statusBeforePause = Status;
        if (Status == DownloadStatus.Downloading)
        {
            pauseRequested = true;
            cts?.Cancel();
        }
        else
        {
            SetStatus(DownloadStatus.Paused);
        }
    }

    public void Resume()
    {
        if (Status != DownloadStatus.Paused)
            return;

        pauseRequested = false;
        Progress = 0;
        SetStatus(DownloadStatus.Queued);
    }

    public void Retry()
    {
        if (Status != DownloadStatus.Error)
            return;

        ErrorMessage = null;
        Progress = 0;
        SetStatus(DownloadStatus.Queued);
    }

    /// <summary>Stops the item if it is running; the queue removes it afterwards.</summary>
    public void Cancel()
    {
        pauseRequested = false;
        cts?.Cancel();
    }

    private void SetStatus(DownloadStatus value)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            Status = value;
        else
            dispatcher.Invoke(() => Status = value);
    }

    #endregion

    #region Download

    internal Task RunAsync() => RunAsync(null);

    internal async Task RunAsync(YoutubeClient client)
    {
        cts = new CancellationTokenSource();
        var token = cts.Token;
        var tempFiles = new List<string>();
        string workingDirectory = null;

        try
        {
            SetStatus(DownloadStatus.Downloading);
            Progress = 0;
            SpeedText = null;
            EtaText = null;

            Directory.CreateDirectory(SaveDirectory);
            // Keep large intermediate files on the selected drive, not the system TEMP drive.
            // A private directory also isolates simultaneous downloads of the same video.
            workingDirectory = Path.Combine(SaveDirectory, $".ypdr-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workingDirectory);

            var signedIn = client == null && YoutubeCookies.HasCookies;
            client ??= GlobalConsts.YoutubeClient;
            StreamManifest manifest;
            try
            {
                manifest = await client.Videos.Streams.GetManifestAsync(Video.Id, token).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (signedIn && ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // YouTube answers some signed-in player requests with 400 while the same request works anonymously.
                client = new YoutubeClient();
                manifest = await client.Videos.Streams.GetManifestAsync(Video.Id, token).ConfigureAwait(false);
            }
            var baseName = FileBaseName(Video);
            var tempBase = Path.Combine(workingDirectory, "media");

            if (IsAudio)
                await DownloadAudioAsync(client, manifest, baseName, tempBase, tempFiles, token).ConfigureAwait(false);
            else
                await DownloadVideoAsync(client, manifest, baseName, tempBase, tempFiles, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (pauseRequested)
            {
                pauseRequested = false;
                SetStatus(DownloadStatus.Paused);
            }
        }
        catch (Exception ex)
        {
            await GlobalConsts.Log(ex.ToString(), $"DownloadItem {Video.Id}").ConfigureAwait(false);
            ErrorMessage = GlobalConsts.FriendlyError(ex);
            SetStatus(DownloadStatus.Error);
        }
        finally
        {
            SpeedText = null;
            EtaText = null;

            foreach (var file in tempFiles)
                try { if (File.Exists(file)) File.Delete(file); } catch { }

            // Never recursively remove a directory that may contain unrelated files.
            try { if (workingDirectory != null) Directory.Delete(workingDirectory); } catch { }

            cts.Dispose();
            cts = null;
        }
    }

    private async Task DownloadVideoAsync(YoutubeClient client, StreamManifest manifest, string baseName, string tempBase, List<string> tempFiles, CancellationToken token)
    {
        var format = Settings.VideoSaveFormat;
        var destination = Path.Combine(SaveDirectory, $"{baseName}.{format}");

        if (Settings.SkipExisting && File.Exists(destination))
        {
            Finish(destination, skipped: true);
            return;
        }

        var video = SelectVideoStream(manifest, format) ?? throw new InvalidOperationException(Loc.Get("NoVideoStream"));
        var audio = SelectAudioStream(manifest) ?? throw new InvalidOperationException(Loc.Get("NoAudioStream"));

        var videoFile = $"{tempBase}-video.{video.Container.Name}";
        var sharedStream = video.Url == audio.Url;
        var audioFile = sharedStream ? videoFile : $"{tempBase}-audio.{audio.Container.Name}";
        var outputFile = $"{tempBase}.{format}";
        tempFiles.AddRange([videoFile, audioFile, outputFile]);

        string captionsFile = null;
        if (Settings.DownloadCaptions)
        {
            try
            {
                var tracks = await client.Videos.ClosedCaptions.GetManifestAsync(Video.Id, token).ConfigureAwait(false);
                var track = tracks.TryGetByLanguage(Settings.CaptionsLanguage);
                if (track != null)
                {
                    captionsFile = $"{tempBase}.srt";
                    tempFiles.Add(captionsFile);
                    await client.Videos.ClosedCaptions.DownloadAsync(track, captionsFile, cancellationToken: token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                captionsFile = null;
                await GlobalConsts.Log(ex.ToString(), "Captions download").ConfigureAwait(false);
            }
        }

        var meter = new ProgressMeter(this, sharedStream ? [video.Size.Bytes] : [video.Size.Bytes, audio.Size.Bytes]);
        if (sharedStream)
            await client.Videos.Streams.DownloadAsync(video, videoFile, meter.For(0), token).ConfigureAwait(false);
        else
            await Task.WhenAll(
                client.Videos.Streams.DownloadAsync(video, videoFile, meter.For(0), token).AsTask(),
                client.Videos.Streams.DownloadAsync(audio, audioFile, meter.For(1), token).AsTask()).ConfigureAwait(false);

        var embedCaptions = captionsFile != null && format == "mkv";
        var arguments = embedCaptions
            ? $"-i \"{videoFile}\" -i \"{audioFile}\" -i \"{captionsFile}\" -map 0:v -map 1:a -map 2 -c copy -c:s srt -y \"{outputFile}\""
            : $"-i \"{videoFile}\" -i \"{audioFile}\" -map 0:v -map 1:a -c copy -y \"{outputFile}\"";

        await RunFfmpegAsync(arguments, token).ConfigureAwait(false);

        destination = MoveToUniquePath(outputFile, SaveDirectory, baseName, format);

        if (captionsFile != null && !embedCaptions)
            File.Copy(captionsFile, Path.ChangeExtension(destination, ".srt"), true);

        Finish(destination);
    }

    private async Task DownloadAudioAsync(YoutubeClient client, StreamManifest manifest, string baseName, string tempBase, List<string> tempFiles, CancellationToken token)
    {
        var audio = SelectAudioStream(manifest) ?? throw new InvalidOperationException(Loc.Get("NoAudioStream"));
        var format = Settings.AudioOnly ? audio.Container.Name : Settings.SaveFormat;

        if (!Settings.AudioOnly && !AudioFormats.Contains(format))
            format = "mp3";

        var destination = Path.Combine(SaveDirectory, $"{baseName}.{format}");
        if (Settings.SkipExisting && File.Exists(destination))
        {
            Finish(destination, skipped: true);
            return;
        }

        var downloadedFile = $"{tempBase}-audio.{audio.Container.Name}";
        tempFiles.Add(downloadedFile);

        var meter = new ProgressMeter(this, [audio.Size.Bytes]);
        await client.Videos.Streams.DownloadAsync(audio, downloadedFile, meter.For(0), token).ConfigureAwait(false);

        var outputFile = downloadedFile;
        if (!Settings.AudioOnly)
        {
            outputFile = $"{tempBase}.{format}";
            tempFiles.Add(outputFile);

            var bitrate = Settings.SetBitrate && !string.IsNullOrWhiteSpace(Settings.Bitrate) && Settings.Bitrate.All(char.IsDigit)
                ? $"-b:a {Settings.Bitrate}k"
                : string.Empty;

            var largeFileOptions = AudioContainerOptions(format);
            await RunFfmpegAsync($"-i \"{downloadedFile}\" -vn {bitrate} {largeFileOptions} -y \"{outputFile}\"", token).ConfigureAwait(false);
        }

        if (Settings.TagAudioFile)
        {
            try
            {
                var tagged = await GlobalConsts.TagFile(Video, Index + 1, outputFile, Playlist).ConfigureAwait(false);

                // For music videos the tagger returns "Artist - Title"; rename with it like the original app did.
                if (!string.IsNullOrWhiteSpace(tagged) && tagged != outputFile && Video is PlaylistVideo playlistVideo)
                {
                    var renamed = new PlaylistVideo(playlistVideo.PlaylistId, playlistVideo.Id, tagged, playlistVideo.Author, playlistVideo.Duration, playlistVideo.Thumbnails);
                    baseName = FileBaseName(renamed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await GlobalConsts.Log(ex.ToString(), "TagFile").ConfigureAwait(false);
            }
        }

        destination = MoveToUniquePath(outputFile, SaveDirectory, baseName, format);
        tempFiles.Remove(outputFile);

        Finish(destination);
    }

    private void Finish(string path, bool skipped = false)
    {
        OutputPath = path;
        Progress = 100;

        try
        {
            if (File.Exists(path))
                SizeText = Format.Bytes(new FileInfo(path).Length);
        }
        catch { }

        SetStatus(skipped ? DownloadStatus.Skipped : DownloadStatus.Done);
    }

    private string FileBaseName(IVideo video)
    {
        var name = GlobalConsts.CleanFileName(Settings.GetFilenameByPattern(video, Index, video.Title, Playlist));
        return string.IsNullOrWhiteSpace(name) ? GlobalConsts.CleanFileName(video.Title) : name.Trim();
    }

    internal static string MoveToUniquePath(string source, string directory, string baseName, string extension)
    {
        for (var i = 0; ; i++)
        {
            var suffix = i == 0 ? string.Empty : $"-{i}";
            var path = Path.Combine(directory, $"{baseName}{suffix}.{extension}");
            try
            {
                File.Move(source, path);
                return path;
            }
            // Another download can claim this name between checking and moving.
            catch (IOException) when (File.Exists(source) && File.Exists(path)) { }
        }
    }

    internal IVideoStreamInfo SelectVideoStream(StreamManifest manifest, string format)
    {
        var streams = manifest.GetVideoStreams().ToList();
        if (streams.Count == 0)
            return null;

        var preferredContainer = format == "mp4" ? YoutubeExplode.Videos.Streams.Container.Mp4 : YoutubeExplode.Videos.Streams.Container.WebM;
        IOrderedEnumerable<IVideoStreamInfo> ordered;

        if (Settings.UseBestQuality)
        {
            ordered = streams.OrderByDescending(x => x.VideoQuality.MaxHeight);
        }
        else
        {
            var target = Settings.Quality.MaxHeight;
            // Closest to the chosen resolution, never going above it when something at or below exists.
            ordered = streams
                .OrderBy(x => x.VideoQuality.MaxHeight > target && streams.Any(s => s.VideoQuality.MaxHeight <= target))
                .ThenBy(x => Math.Abs(x.VideoQuality.MaxHeight - target));
        }

        if (Settings.PreferHighestFPS)
            ordered = ordered.ThenByDescending(x => x.VideoQuality.Framerate);

        return ordered
            .ThenByDescending(x => x.Container == preferredContainer)
            .ThenByDescending(x => x.Bitrate.BitsPerSecond)
            .FirstOrDefault();
    }

    internal IStreamInfo SelectAudioStream(StreamManifest manifest)
    {
        var audio = manifest.GetAudioOnlyStreams().ToList();
        if (audio.Count == 0)
        {
            // FFmpeg can extract the audio from a combined stream for video/conversion modes.
            // Original-audio mode must never silently save a video file as audio.
            return Settings.AudioOnly ? null : manifest.GetMuxedStreams()
                .OrderByDescending(x => x.Bitrate.BitsPerSecond).FirstOrDefault();
        }

        var language = Settings.VideoLanguage;
        if (!string.IsNullOrWhiteSpace(language) && language != "default")
        {
            var inLanguage = audio.Where(x => x.AudioLanguage.HasValue && x.AudioLanguage.Value.Code.Contains(language)).ToList();
            if (inLanguage.Count > 0)
                return inLanguage.GetWithHighestBitrate();
        }

        var defaults = audio.Where(x => x.IsAudioLanguageDefault == true).ToList();
        return defaults.Count > 0 ? defaults.GetWithHighestBitrate() : audio.GetWithHighestBitrate();
    }

    // Ordinary RIFF WAV headers cannot represent audio larger than 4 GiB.
    internal static string AudioContainerOptions(string format) => format == "wav" ? "-rf64 auto" : string.Empty;

    internal async Task RunFfmpegAsync(string arguments, CancellationToken token)
    {
        if (!GlobalConsts.FFmpegExists)
            throw new FileNotFoundException(Loc.Get("FfmpegMissingError"));

        SetStatus(DownloadStatus.Converting);
        Progress = 0;
        SpeedText = null;
        EtaText = null;

        var limited = GlobalConsts.settings.LimitConversions;
        if (limited)
            await GlobalConsts.ConversionsLocker.WaitAsync(token).ConfigureAwait(false);

        try
        {
            var duration = Video.Duration?.TotalSeconds ?? 0;
            var errorTail = new FixedQueue<string>(8);

            using var ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GlobalConsts.FFmpegFilePath,
                    Arguments = $"-hide_banner -nostdin {arguments}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            ffmpeg.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null)
                    return;

                lock (errorTail)
                    errorTail.Enqueue(e.Data);

                var match = FfmpegTimeRegex().Match(e.Data);
                if (match.Success && duration > 0 && TimeSpan.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var time))
                {
                    Progress = Math.Min(99, time.TotalSeconds * 100 / duration);
                    OnPropertyChanged(nameof(IsIndeterminate));
                }
            };

            ffmpeg.Start();
            ffmpeg.BeginErrorReadLine();

            try
            {
                await ffmpeg.WaitForExitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { ffmpeg.Kill(true); } catch { }
                await ffmpeg.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            if (ffmpeg.ExitCode != 0)
            {
                string tail;
                lock (errorTail)
                    tail = string.Join(Environment.NewLine, errorTail);

                await GlobalConsts.Log($"ffmpeg {arguments}{Environment.NewLine}{tail}", "ffmpeg").ConfigureAwait(false);
                throw new InvalidOperationException(Loc.Get("ConversionFailed"));
            }
        }
        finally
        {
            if (limited)
                GlobalConsts.ConversionsLocker.Release();
        }
    }

    [GeneratedRegex(@"time=(\d+:\d+:\d+(?:\.\d+)?)")]
    private static partial Regex FfmpegTimeRegex();

    #endregion

    /// <summary>
    /// Turns per-stream download fractions into overall progress, speed and time left.
    /// </summary>
    private sealed class ProgressMeter
    {
        private readonly DownloadItem item;
        private readonly long[] sizes;
        private readonly double[] fractions;
        private readonly long total;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly object gate = new();
        private long lastBytes;
        private double lastTime;
        private double speed;

        public ProgressMeter(DownloadItem item, long[] sizes)
        {
            this.item = item;
            this.sizes = sizes;
            fractions = new double[sizes.Length];
            total = Math.Max(1, sizes.Sum());
        }

        public IProgress<double> For(int index) => new Reporter(this, index);

        private void Report(int index, double fraction)
        {
            lock (gate)
            {
                fractions[index] = fraction;
                var bytes = (long)sizes.Select((size, i) => size * fractions[i]).Sum();
                item.Progress = bytes * 100.0 / total;

                var now = clock.Elapsed.TotalSeconds;
                var elapsed = now - lastTime;
                if (elapsed < 0.5)
                    return;

                var current = (bytes - lastBytes) / elapsed;
                speed = speed <= 0 ? current : speed * 0.7 + current * 0.3;
                lastBytes = bytes;
                lastTime = now;

                item.SpeedText = $"{Format.Bytes((long)speed)}/s";
                item.EtaText = speed > 0 ? Loc.Format("TimeLeft", Format.Duration(TimeSpan.FromSeconds((total - bytes) / speed))) : null;
            }
        }

        private sealed class Reporter(ProgressMeter meter, int index) : IProgress<double>
        {
            public void Report(double value) => meter.Report(index, value);
        }
    }
}
