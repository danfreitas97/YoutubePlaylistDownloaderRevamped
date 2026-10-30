using System.Drawing;
using System.Drawing.Imaging;

namespace YoutubePlaylistDownloader;

static class GlobalConsts
{
    #region Const Variables
    public static readonly string TempFolderPath;
    public static readonly string CurrentDir;
    private static readonly string ConfigFilePath;
    private static readonly string ErrorFilePath;
    public static readonly Version VERSION = typeof(GlobalConsts).Assembly.GetName().Version;
    public static readonly string ChannelSubscriptionsFilePath;
    private static DownloadSettings downloadSettings;
    public static readonly string DownloadSettingsFilePath;
    private static SemaphoreSlim conversionLocker;
    public static Objects.Settings settings;

    // Where the update check and the update setup are fetched from.
    public const string RepositoryRawUrl = "https://raw.githubusercontent.com/danfreitas97/YoutubePlaylistDownloaderRevamped/main/YPDRApp/";
    public const string RepositoryUrl = "https://github.com/danfreitas97/YoutubePlaylistDownloaderRevamped";

    public static YoutubeClient YoutubeClient => YoutubeCookies.HasCookies ? new(YoutubeCookies.Current) : new();

    /// <summary>A message the user can act on, instead of the library's technical text.</summary>
    public static string FriendlyError(Exception ex) => ex switch
    {
        YoutubeExplode.Exceptions.RequestLimitExceededException => Loc.Get("RateLimited"),
        HttpRequestException => Loc.Get("NoConnection"),
        _ => ex.Message
    };
    public static SemaphoreSlim ConversionsLocker { get => conversionLocker; set => conversionLocker ??= value; }

    /// <summary>
    /// ffmpeg.exe next to the executable, then the copy the app downloaded itself, then one on the PATH.
    /// </summary>
    public static string FFmpegFilePath
    {
        get
        {
            var local = Path.Combine(CurrentDir, "ffmpeg.exe");
            if (File.Exists(local))
                return local;

            if (File.Exists(FfmpegService.InstallPath))
                return FfmpegService.InstallPath;

            var fromPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(dir => Path.Combine(dir.Trim(), "ffmpeg.exe"))
                .FirstOrDefault(File.Exists);

            return fromPath ?? FfmpegService.InstallPath;
        }
    }

    public static bool FFmpegExists => File.Exists(FFmpegFilePath);

    public static DownloadSettings DownloadSettings
    {
        get
        {
            downloadSettings ??= DefaultDownloadSettings();
            return downloadSettings;
        }
        set
        {
            if (value != null)
            {
                downloadSettings = value;
                SaveDownloadSettings();
            }
        }
    }

    #endregion

    static GlobalConsts()
    {
        JsonConvert.DefaultSettings = () =>
        {
            var settings = new JsonSerializerSettings();
            settings.Converters.Add(new VideoQualityConverter());
            return settings;
        };
        CurrentDir = new FileInfo(Assembly.GetEntryAssembly().Location).Directory.ToString();
        var appDataPath = AppDataPath;
        ConfigFilePath = string.Concat(appDataPath, "Settings.json");
        ErrorFilePath = string.Concat(appDataPath, "Errors.txt");
        DownloadSettingsFilePath = string.Concat(appDataPath, "DownloadSettings.json");
        ChannelSubscriptionsFilePath = string.Concat(appDataPath, "Subscriptions.ypds");

        if (!Directory.Exists(appDataPath))
            Directory.CreateDirectory(appDataPath);

        settings = DefaultSettings();
        TempFolderPath = string.Concat(Path.GetTempPath(), "YoutubePlaylistDownloaderRevamped\\");
    }

    /// <summary>
    /// Settings, logs and cookies. Separate from the original app's folder so both can be installed side by side.
    /// </summary>
    public static string AppDataPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YouTube Playlist Downloader Revamped") + "\\";

    public const string DefaultLanguage = "Português (BR)";

    #region Const Methods

    public static Objects.Settings DefaultSettings() =>
        new("System", "Red", DefaultLanguage, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), false, false, true, TimeSpan.FromMinutes(1), true, 20, 2, true, true)
        {
            ConcurrentDownloads = 3,
            NotifyWhenDone = true,
            DetectClipboard = true
        };

    public static DownloadSettings DefaultDownloadSettings() =>
        new("mp3", false, YoutubeHelpers.High1080, true, false, false, false, "192", false, "en", false, false, 0, 0, false, true, false, true, 4, "$title", true, "mp4", "default")
        {
            UseBestQuality = true
        };

    public static void SaveConsts()
    {
        try
        {
            File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(settings));
            SaveDownloadSettings();
        }
        catch (Exception ex)
        {
            Log(ex.ToString(), "SaveConsts").Wait();
        }
    }

    public static void SaveSettings()
    {
        try
        {
            File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(settings));
        }
        catch (Exception ex)
        {
            Log(ex.ToString(), "SaveSettings").Wait();
        }
    }

    public static void RestoreDefualts()
    {
        Log("Restoring defaults", "RestoreDefaults at GlobalConsts").Wait();
        settings = DefaultSettings();
        DownloadSettings = DefaultDownloadSettings();
        SaveConsts();
    }

    public static void LoadConsts()
    {
        if (!File.Exists(ConfigFilePath))
        {
            Log("Config file does not exist, restoring defaults", "LoadConsts at GlobalConsts").Wait();
            RestoreDefualts();
        }
        else
        {
            try
            {
                settings = JsonConvert.DeserializeObject<Objects.Settings>(File.ReadAllText(ConfigFilePath));
                LoadDownloadSettings();
            }
            catch (Exception ex)
            {
                Log(ex.ToString(), "LoadConsts at GlobalConsts").Wait();
                RestoreDefualts();
            }
        }

        if (settings.ConcurrentDownloads <= 0)
            settings.ConcurrentDownloads = 3;

        if (string.IsNullOrWhiteSpace(settings.SaveDirectory))
            settings.SaveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        if (settings.MaximumConversionsCount <= 0)
            settings.MaximumConversionsCount = 20;

        if (settings.ActualConversionsLimit <= 0 || settings.ActualConversionsLimit > settings.MaximumConversionsCount)
            settings.ActualConversionsLimit = Math.Max(1, Environment.ProcessorCount - 1);

        ConversionsLocker = new SemaphoreSlim(settings.ActualConversionsLimit, settings.MaximumConversionsCount);
    }

    public static void CreateTempFolder()
    {
        try
        {
            if (!Directory.Exists(TempFolderPath))
                Directory.CreateDirectory(TempFolderPath);
        }
        catch (Exception ex)
        {
            Log($"Failed to create temp folder, {ex}", "CreateTempFolder at GlobalConsts").Wait();
        }
    }

    public static void CleanTempFolder()
    {
        if (Directory.Exists(TempFolderPath))
        {
            DirectoryInfo di = new(TempFolderPath);

            foreach (var file in di.GetFiles())
                try { file.Delete(); } catch { };

            foreach (var dir in di.GetDirectories())
                try { dir.Delete(true); } catch { };
        }
    }

    private static readonly SemaphoreSlim logLocker = new(1, 1);
    public static async Task Log(string message, object sender)
    {
        await logLocker.WaitAsync().ConfigureAwait(false);
        try
        {
            using StreamWriter sw = new(ErrorFilePath, true);
            await sw.WriteLineAsync($"[{DateTime.Now.ToUniversalTime()}], [{sender}]:\n\n{message}\n\n").ConfigureAwait(false);
        }
        catch
        {
            // Logging must never take the app down.
        }
        finally
        {
            logLocker.Release();
        }
    }

    public static string CleanFileName(string filename)
    {
        var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        var invalidReStr = string.Format(@"[{0}]+", invalidChars);

        var reservedWords = new[]
        {
            "CON", "PRN", "AUX", "CLOCK$", "NUL", "COM0", "COM1", "COM2", "COM3", "COM4",
            "COM5", "COM6", "COM7", "COM8", "COM9", "LPT0", "LPT1", "LPT2", "LPT3", "LPT4",
            "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        var sanitisedNamePart = Regex.Replace(filename, invalidReStr, "_");
        foreach (var reservedWord in reservedWords)
        {
            var reservedWordPattern = string.Format("^{0}\\.", reservedWord);
            sanitisedNamePart = Regex.Replace(sanitisedNamePart, reservedWordPattern, "_reservedWord_.", RegexOptions.IgnoreCase);
        }

        return sanitisedNamePart;
    }

    static void CropAndSaveImage(byte[] imageBytes, string imagePath)
    {
        using var imageBuffer = new MemoryStream(imageBytes);
        using var image = System.Drawing.Image.FromStream(imageBuffer);
        var cropRectangle = new Rectangle((image.Width - image.Height) / 2, 0, image.Height, image.Height);
        using var bitmap = new Bitmap(cropRectangle.Width, cropRectangle.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(image, new Rectangle(0, 0, bitmap.Width, bitmap.Height), cropRectangle, GraphicsUnit.Pixel);
        bitmap.Save(imagePath, ImageFormat.Jpeg);
    }

    static readonly string[] ignoredComments = ["Auto-generated by YouTube.", "Provided to YouTube by"];
    internal static readonly string[] IgnoredGeneres = ["download", "out now", "mostercat", "video", "lyric", "release", "ncs", "records"];
    internal static readonly string[] ArtistsSeparators = ["&", "feat.", "feat", "ft.", " ft ", "Feat.", " x ", " X "];
    internal static readonly string[] VideoTitleSeparators = [" - ", " — "];

    static async Task<string> TagMusicFile(Video fullVideo, string file, int vIndex)
    {
        // Index YouTube Auto Generated Description
        var description = fullVideo.Description.Split("\n");
        var title = string.Empty;
        var artists = new List<string>();
        var album = string.Empty;
        var releaseDate = default(DateTime);
        var comment = new StringBuilder();
        var commentIndex = 0;

        try
        {
            foreach (var line in description)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    goto loopEnd;
                }

                if (line.Contains('·'))
                {
                    var titleAndArtists = line.Split('·').Select(x => x.Trim());

                    title = titleAndArtists.FirstOrDefault();
                    artists.AddRange(titleAndArtists.Skip(1));
                    album = description.ElementAtOrDefault(commentIndex + 2);

                    if (album != null && !album.Contains("Auto-generated by YouTube.") && !album.StartsWith("Released on:"))
                    {
                        goto loopEnd;
                    }
                    else
                    {
                        album = string.Empty;
                    }
                }

                if (line.StartsWith("Released on:"))
                {
                    releaseDate = DateTime.Parse(line.Split(":").ElementAtOrDefault(1));
                    goto loopEnd;
                }

                if (!string.IsNullOrWhiteSpace(album) && line.StartsWith(album))
                {
                    goto loopEnd;
                }

                if (ignoredComments.Any(x => line.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                {
                    goto loopEnd;
                }

                comment.AppendLine(line);

            loopEnd:
                commentIndex++;
            }
        }
        catch (Exception e)
        {
            await Log(e.ToString(), "TagMusicFile inside description loop").ConfigureAwait(false);
            return null;
        }

        if (releaseDate == default)
        {
            releaseDate = fullVideo.UploadDate.Date;
        }

        using (var tagLibFile = TagLib.File.Create(file))
        {
            tagLibFile.Tag.Title = title;
            tagLibFile.Tag.Performers = [.. artists];
            tagLibFile.Tag.Year = (uint)releaseDate.Year;
            tagLibFile.Tag.Comment = comment.ToString();
            tagLibFile.Tag.Album = album;
            tagLibFile.Tag.Track = (uint)vIndex;

            try
            {
                var picturePath = $"{TempFolderPath}{CleanFileName(fullVideo.Title)}.jpg";

                using (var httpClient = new HttpClient())
                {
                    var pictureContent = await httpClient.GetByteArrayAsync($"https://img.youtube.com/vi/{fullVideo.Id}/maxresdefault.jpg").ConfigureAwait(false);
                    CropAndSaveImage(pictureContent, picturePath);
                }

                tagLibFile.Tag.Pictures = [new TagLib.Picture(picturePath)];
            }
            catch (Exception ex)
            {
                await Log("Failed to add picture to file at TagMusicFile", ex.ToString()).ConfigureAwait(false);
            }

            tagLibFile.Save();
        }

        return $"{string.Join(", ", artists)} - {title}";
    }

    public static async Task<string> TagFileBasedOnTitle(IVideo video, int index, string file, FullPlaylist playlist = null)
    {
        var title = video.Title.Replace("—", "-");
        var genre = title.Split('[', ']').ElementAtOrDefault(1);

        if (genre == null)
        {
            genre = string.Empty;
        }
        else if (genre.Length >= title.Length)
        {
            genre = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(genre))
        {
            title = title.Replace($"[{genre}]", string.Empty);
            var stringToRemove = title.Split('[', ']', '【', '】').ElementAtOrDefault(1);

            if (!string.IsNullOrWhiteSpace(stringToRemove))
            {
                title = title.Replace($"[{stringToRemove}]", string.Empty);
            }
        }

        title = title.TrimStart(' ', '-', '[', ']').TrimEnd();

        using (var tagLibFile = TagLib.File.Create(file))
        {
            tagLibFile.Tag.Album = playlist?.BasePlaylist?.Title;
            tagLibFile.Tag.Track = (uint)index;
            tagLibFile.Tag.AlbumArtists = [playlist?.BasePlaylist?.Author?.ChannelTitle];

            if (IgnoredGeneres.Any(genre.ToLower().Contains))
            {
                genre = string.Empty;
            }
            else
            {
                tagLibFile.Tag.Genres = genre.Split('/', '\\');
            }

            if (TryGetSongTitleAndPerformersFromTitle(title, out string songTitle, out string[] songPerformers))
            {
                tagLibFile.Tag.Title = songTitle;
                tagLibFile.Tag.Performers = songPerformers;
            }

            try
            {
                var picturePath = $"{TempFolderPath}{CleanFileName(video.Title)}.jpg";

                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync($"https://img.youtube.com/vi/{video.Id}/maxresdefault.jpg").ConfigureAwait(false);

                    using (var pictureStream = File.Create(picturePath))
                    {
                        await response.Content.CopyToAsync(pictureStream).ConfigureAwait(false);
                    }
                }

                tagLibFile.Tag.Pictures = [new TagLib.Picture(picturePath)];
            }
            catch (Exception ex)
            {
                await Log("Failed to add picture to file at TagFileBasedOnTitle", ex.ToString()).ConfigureAwait(false);
            }

            tagLibFile.Save();
        }

        return file;
    }

    public static bool TryGetSongTitleAndPerformersFromTitle(string title, out string songTitle, out string[] songPerformers)
    {
        songTitle = null;
        songPerformers = null;

        var index = title.LastIndexOf('-');

        if (index > 0)
        {
            songTitle = title[(index + 1)..].Trim(' ', '-');

            if (string.IsNullOrWhiteSpace(songTitle))
            {
                index = title.IndexOf('-');

                if (index > 0)
                {
                    songTitle = title[(index + 1)..].Trim(' ', '-');
                }
            }

            songPerformers = title[..(index - 1)].Trim().Split(ArtistsSeparators, StringSplitOptions.RemoveEmptyEntries);

            return true;
        }

        return false;
    }

    public static async Task<string> TagFile(IVideo video, int vIndex, string file, FullPlaylist playlist = null)
    {
        ArgumentNullException.ThrowIfNull(video);

        if (!VideoTitleSeparators.Any(video.Title.Contains))
        {
            var fullVideo = await YoutubeClient.Videos.GetAsync(video.Id).ConfigureAwait(false);

            if (fullVideo.Description.Contains("Auto-generated by YouTube."))
            {
                var fileName = await TagMusicFile(fullVideo, file, vIndex);

                if (fileName != null)
                {
                    return fileName;
                }
            }
        }

        return await TagFileBasedOnTitle(video, vIndex, file, playlist);
    }

    private static void LoadDownloadSettings()
    {
        if (File.Exists(DownloadSettingsFilePath))
        {
            try
            {
                downloadSettings = JsonConvert.DeserializeObject<DownloadSettings>(File.ReadAllText(DownloadSettingsFilePath));
            }
            catch (Exception ex)
            {
                Log(ex.ToString(), "LoadDownloadSettings at GlobalConsts").Wait();
                try
                {
                    if (File.Exists(DownloadSettingsFilePath))
                        File.Delete(DownloadSettingsFilePath);
                }
                catch (Exception ex2)
                {
                    Log(ex2.ToString(), "Delete download settings file path").Wait();
                }
                downloadSettings = DefaultDownloadSettings();
            }
        }
        else
        {
            downloadSettings = DefaultDownloadSettings();
        }
    }

    public static void SaveDownloadSettings()
    {
        try
        {
            File.WriteAllText(DownloadSettingsFilePath, JsonConvert.SerializeObject(downloadSettings));
        }
        catch (Exception ex)
        {
            Log(ex.ToString(), "SaveDownloadSettings at GlobalConsts").Wait();
        }
    }

    #endregion
}
