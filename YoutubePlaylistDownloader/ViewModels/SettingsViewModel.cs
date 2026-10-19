namespace YoutubePlaylistDownloader.ViewModels;

/// <summary>
/// The "Settings" screen. Every change is written to disk right away.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] Resolutions = ["4320p", "2160p", "1440p", "1080p", "720p", "480p", "360p", "240p", "144p"];

    private readonly MainViewModel main;
    private bool loading;

    public SettingsViewModel(MainViewModel main)
    {
        this.main = main;
        CaptionLanguages = LanguageCodes.Sorted.ToList();
        ConcurrentOptions = Enumerable.Range(1, 8).Select(x => new Option(x.ToString(), x.ToString())).ToList();
        ConversionOptions = Enumerable.Range(1, Math.Max(2, Environment.ProcessorCount)).Select(x => new Option(x.ToString(), x.ToString())).ToList();
        Loc.LanguageChanged += (s, e) => Refresh();
        ThemeService.ThemeChanged += (s, e) => OnPropertyChanged(nameof(ThemeMode));
        Refresh();
    }

    public string VersionText => Loc.Format("VersionFmt", GlobalConsts.VERSION);
    public IReadOnlyList<LanguageOption> Languages => main.Languages;
    public IReadOnlyList<Option> CaptionLanguages { get; }
    public IReadOnlyList<Option> ConcurrentOptions { get; }
    public IReadOnlyList<Option> ConversionOptions { get; }

    [ObservableProperty] private IReadOnlyList<Option> qualityOptions = [];
    [ObservableProperty] private IReadOnlyList<Option> audioLanguages = [];
    [ObservableProperty] private IReadOnlyList<Option> videoFormats = [];
    [ObservableProperty] private IReadOnlyList<Option> audioFormats = [];
    [ObservableProperty] private IReadOnlyList<Option> bitrates = [];

    // Downloads
    [ObservableProperty] private string saveDirectory;
    [ObservableProperty] private Option concurrentDownloads;
    [ObservableProperty] private bool skipExisting;
    [ObservableProperty] private bool detectClipboard;
    [ObservableProperty] private bool notifyWhenDone;
    [ObservableProperty] private bool openFolderWhenDone;
    [ObservableProperty] private bool confirmExit;

    // File names
    [ObservableProperty] private string filenamePattern;
    [ObservableProperty] private string patternPreview;
    [ObservableProperty] private bool savePlaylistsInDifferentDirectories;

    // Video
    [ObservableProperty] private Option preferredQuality;
    [ObservableProperty] private Option videoFormat;
    [ObservableProperty] private bool preferHighestFps;
    [ObservableProperty] private bool downloadCaptions;
    [ObservableProperty] private Option captionsLanguage;
    [ObservableProperty] private Option audioLanguage;

    // Audio
    [ObservableProperty] private Option audioFormat;
    [ObservableProperty] private Option bitrate;
    [ObservableProperty] private bool tagAudioFile;

    // Advanced
    [ObservableProperty] private bool limitConversions;
    [ObservableProperty] private Option conversionsLimit;
    [ObservableProperty] private bool checkForUpdates;

    public string ThemeMode
    {
        get => ThemeService.Mode;
        set
        {
            if (value != null && value != ThemeService.Mode)
                ThemeService.Apply(value);

            OnPropertyChanged();
        }
    }

    public string CurrentLanguage
    {
        get => Loc.Current;
        set
        {
            if (value != null && value != Loc.Current)
                Loc.Apply(value);

            OnPropertyChanged();
        }
    }

    public void Refresh()
    {
        loading = true;
        var s = GlobalConsts.settings;
        var d = GlobalConsts.DownloadSettings;

        QualityOptions = [new Option("best", Loc.Get("BestQuality")), .. Resolutions.Select(x => new Option(x, x))];
        AudioLanguages = [new Option("default", Loc.Get("DefaultTrack")), .. CaptionLanguages];
        VideoFormats = [new Option("mp4", "MP4"), new Option("mkv", "MKV")];
        AudioFormats = new[] { "mp3", "m4a", "aac", "opus", "flac", "wav", "ogg", "webm" }.Select(x => new Option(x, x.ToUpperInvariant())).ToList();
        Bitrates = [new Option("original", Loc.Get("OriginalBitrate")), .. new[] { "320", "256", "192", "128" }.Select(x => new Option(x, $"{x} kbps"))];

        SaveDirectory = s.SaveDirectory;
        ConcurrentDownloads = ConcurrentOptions.FirstOrDefault(x => x.Key == s.ConcurrentDownloads.ToString()) ?? ConcurrentOptions[2];
        SkipExisting = d.SkipExisting;
        DetectClipboard = s.DetectClipboard;
        NotifyWhenDone = s.NotifyWhenDone;
        OpenFolderWhenDone = d.OpenDestinationFolderWhenDone;
        ConfirmExit = s.ConfirmExit;

        FilenamePattern = d.FilenamePattern;
        SavePlaylistsInDifferentDirectories = d.SavePlaylistsInDifferentDirectories;

        PreferredQuality = d.UseBestQuality ? QualityOptions[0] : QualityOptions.FirstOrDefault(x => x.Key == d.Quality.Label) ?? QualityOptions[0];
        VideoFormat = VideoFormats.FirstOrDefault(x => x.Key == d.VideoSaveFormat) ?? VideoFormats[0];
        PreferHighestFps = d.PreferHighestFPS;
        DownloadCaptions = d.DownloadCaptions;
        CaptionsLanguage = CaptionLanguages.FirstOrDefault(x => x.Key == d.CaptionsLanguage) ?? CaptionLanguages.First(x => x.Key == "en");
        AudioLanguage = AudioLanguages.FirstOrDefault(x => x.Key == d.VideoLanguage) ?? AudioLanguages[0];

        AudioFormat = AudioFormats.FirstOrDefault(x => x.Key == d.SaveFormat) ?? AudioFormats[0];
        Bitrate = d.SetBitrate ? Bitrates.FirstOrDefault(x => x.Key == d.Bitrate) ?? Bitrates[0] : Bitrates[0];
        TagAudioFile = d.TagAudioFile;

        LimitConversions = s.LimitConversions;
        ConversionsLimit = ConversionOptions.FirstOrDefault(x => x.Key == s.ActualConversionsLimit.ToString()) ?? ConversionOptions[^1];
        CheckForUpdates = s.CheckForProgramUpdates;

        UpdatePatternPreview();
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(AccountStatus));
        loading = false;
    }

    private void Save(Action<Objects.Settings, DownloadSettings> write)
    {
        if (loading)
            return;

        write(GlobalConsts.settings, GlobalConsts.DownloadSettings);
        GlobalConsts.SaveConsts();
    }

    partial void OnConcurrentDownloadsChanged(Option value) => Save((s, d) =>
    {
        s.ConcurrentDownloads = int.Parse(value.Key);
        DownloadQueue.Instance.Pump();
    });

    partial void OnSkipExistingChanged(bool value) => Save((s, d) => d.SkipExisting = value);
    partial void OnDetectClipboardChanged(bool value) => Save((s, d) => s.DetectClipboard = value);
    partial void OnNotifyWhenDoneChanged(bool value) => Save((s, d) => s.NotifyWhenDone = value);
    partial void OnOpenFolderWhenDoneChanged(bool value) => Save((s, d) => d.OpenDestinationFolderWhenDone = value);
    partial void OnConfirmExitChanged(bool value) => Save((s, d) => s.ConfirmExit = value);
    partial void OnSavePlaylistsInDifferentDirectoriesChanged(bool value) => Save((s, d) => d.SavePlaylistsInDifferentDirectories = value);
    partial void OnPreferHighestFpsChanged(bool value) => Save((s, d) => d.PreferHighestFPS = value);
    partial void OnDownloadCaptionsChanged(bool value) => Save((s, d) => d.DownloadCaptions = value);
    partial void OnTagAudioFileChanged(bool value) => Save((s, d) => d.TagAudioFile = value);
    partial void OnCheckForUpdatesChanged(bool value) => Save((s, d) => s.CheckForProgramUpdates = value);

    partial void OnFilenamePatternChanged(string value)
    {
        UpdatePatternPreview();
        Save((s, d) => d.FilenamePattern = string.IsNullOrWhiteSpace(value) ? "$title" : value);
    }

    partial void OnPreferredQualityChanged(Option value) => Save((s, d) =>
    {
        d.UseBestQuality = value.Key == "best";
        if (!d.UseBestQuality)
            d.Quality = YoutubeHelpers.FromLabel(value.Key, 30);
    });

    partial void OnVideoFormatChanged(Option value) => Save((s, d) => d.VideoSaveFormat = value.Key);
    partial void OnCaptionsLanguageChanged(Option value) => Save((s, d) => d.CaptionsLanguage = value.Key);
    partial void OnAudioLanguageChanged(Option value) => Save((s, d) => d.VideoLanguage = value.Key);
    partial void OnAudioFormatChanged(Option value) => Save((s, d) => d.SaveFormat = value.Key);

    partial void OnBitrateChanged(Option value) => Save((s, d) =>
    {
        d.SetBitrate = value.Key != "original";
        if (d.SetBitrate)
            d.Bitrate = value.Key;
    });

    partial void OnLimitConversionsChanged(bool value) => Save((s, d) => s.LimitConversions = value);

    partial void OnConversionsLimitChanged(Option value) => Save((s, d) =>
    {
        var limit = int.Parse(value.Key);
        var locker = GlobalConsts.ConversionsLocker;
        var delta = limit - s.ActualConversionsLimit;

        // Grow or shrink the running semaphore so the new limit applies without a restart.
        if (delta > 0)
            locker.Release(Math.Min(delta, s.MaximumConversionsCount - locker.CurrentCount));
        else
            for (var i = 0; i < -delta; i++)
                _ = locker.WaitAsync();

        s.ActualConversionsLimit = limit;
    });

    private void UpdatePatternPreview()
    {
        var sample = (FilenamePattern ?? "$title")
            .Replace("$title", "Infected Mushroom - Spitfire")
            .Replace("$index", "3")
            .Replace("$artist", "Infected Mushroom")
            .Replace("$songtitle", "Spitfire")
            .Replace("$channel", "Monstercat")
            .Replace("$videoid", "dQw4w9WgXcQ")
            .Replace("$playlist", "Playlist")
            .Replace("$genre", "Psytrance");

        PatternPreview = $"{GlobalConsts.CleanFileName(string.IsNullOrWhiteSpace(sample) ? "Infected Mushroom - Spitfire" : sample)}.{GlobalConsts.DownloadSettings.VideoSaveFormat}";
    }

    [RelayCommand]
    private void InsertToken(string token) => FilenamePattern = $"{FilenamePattern}{token}";

    [RelayCommand]
    private void ResetPattern() => FilenamePattern = "$title";

    [RelayCommand]
    private void ChangeFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(SaveDirectory) ? SaveDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog() == true)
        {
            SaveDirectory = dialog.FolderName;
            GlobalConsts.settings.SaveDirectory = dialog.FolderName;
            GlobalConsts.SaveSettings();
        }
    }

    [RelayCommand]
    private void OpenSaveFolder() => MainViewModel.OpenFolder(SaveDirectory);

    #region YouTube account (cookies)

    public bool HasCookies => YoutubeCookies.HasCookies;

    public string AccountStatus => Loc.Get(HasCookies ? "SignedIn" : "NoCookies");

    [RelayCommand]
    private Task SignIn() => SignInAsync();

    /// <summary>Opens Google's sign-in in an embedded browser; true when a signed-in session was captured.</summary>
    public async Task<bool> SignInAsync()
    {
        var window = new Views.SignInWindow { Owner = Application.Current.MainWindow };
        var signedIn = window.ShowDialog() == true;

        if (window.RuntimeMissing)
            await main.Dialog.ShowMessageAsync(Loc.Get("Error"), Loc.Get("SignInUnavailable"));
        else if (signedIn)
            main.ShowSnack(Loc.Get("SignedIn"));

        OnPropertyChanged(nameof(HasCookies));
        OnPropertyChanged(nameof(AccountStatus));
        return signedIn;
    }

    [RelayCommand]
    private async Task ImportCookies()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "cookies.txt|*.txt|*.*|*.*" };
        if (dialog.ShowDialog() != true)
            return;

        int count;
        try
        {
            count = YoutubeCookies.Import(dialog.FileName);
        }
        catch (Exception ex)
        {
            await GlobalConsts.Log(ex.ToString(), "ImportCookies");
            count = 0;
        }

        if (count == 0)
            await main.Dialog.ShowMessageAsync(Loc.Get("Error"), Loc.Get("CookiesInvalid"));
        else
            main.ShowSnack(Loc.Get("SignedIn"));

        OnPropertyChanged(nameof(HasCookies));
        OnPropertyChanged(nameof(AccountStatus));
    }

    [RelayCommand]
    private void RemoveCookies()
    {
        YoutubeCookies.Clear();
        OnPropertyChanged(nameof(HasCookies));
        OnPropertyChanged(nameof(AccountStatus));
    }

    #endregion

    [RelayCommand]
    private Task CheckUpdatesNow() => main.CheckForUpdatesAsync(silent: false);

    [RelayCommand]
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            GlobalConsts.Log(ex.ToString(), "OpenUrl").ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void OpenLog()
    {
        MainViewModel.OpenFolder(GlobalConsts.AppDataPath);
    }

    [RelayCommand]
    private async Task RestoreDefaults()
    {
        if (!await main.Dialog.ConfirmAsync(Loc.Get("RestoreDefaults"), Loc.Get("RestoreDefaultsConfirm")))
            return;

        var language = GlobalConsts.settings.Language;
        GlobalConsts.RestoreDefualts();
        GlobalConsts.settings.Language = language;
        GlobalConsts.SaveSettings();
        ThemeService.Apply(ThemeService.System);
        Refresh();
        main.Download.LoadFromSettings();
    }
}
