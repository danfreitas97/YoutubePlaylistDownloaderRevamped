namespace YoutubePlaylistDownloader.ViewModels;

/// <summary>
/// The "Download" screen: paste a link, look at what it contains, choose what and how to download.
/// </summary>
public partial class DownloadViewModel : ObservableObject
{
    public const string StateEmpty = "empty";
    public const string StateLoading = "loading";
    public const string StateLoaded = "loaded";

    private static readonly string[] VideoFormats = ["mp4", "mkv"];
    private static readonly string[] AudioFormats = ["mp3", "m4a", "aac", "opus", "flac", "wav", "ogg", "webm"];
    private static readonly string[] Resolutions = ["4320p", "2160p", "1440p", "1080p", "720p", "480p", "360p", "240p", "144p"];
    private static readonly string[] Bitrates = ["320", "256", "192", "128"];
    private const string OriginalKey = "original";
    private const string BestKey = "best";

    private readonly MainViewModel main;
    private CancellationTokenSource loadCts;
    private string dismissedClipboardLink;
    private bool suppressSettingsWrite;

    public DownloadViewModel(MainViewModel main)
    {
        this.main = main;
        VideosView = CollectionViewSource.GetDefaultView(Videos);
        VideosView.Filter = FilterVideo;
        LoadFromSettings();
        Loc.LanguageChanged += (s, e) =>
        {
            RefreshTexts();
            Library = [.. Library];
        };

        YoutubeCookies.Changed += (s, e) =>
        {
            OnPropertyChanged(nameof(IsSignedIn));
            _ = LoadLibraryAsync();
        };
        _ = LoadLibraryAsync();
    }

    #region Account library

    private CancellationTokenSource libraryCts;

    // Collapsed, the library shows this many playlists so it doesn't push the rest of the screen away.
    private const int CollapsedLibraryCount = 8;

    /// <summary>Liked music and the playlists of the signed-in account, shown under the link field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleLibrary), nameof(CanExpandLibrary), nameof(LibraryToggleText))]
    private IReadOnlyList<LibraryPlaylist> library = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleLibrary), nameof(LibraryToggleText))]
    private bool isLibraryExpanded;

    [ObservableProperty] private bool isLibraryLoading;
    [ObservableProperty] private string libraryError;

    public bool IsSignedIn => YoutubeCookies.HasCookies;

    public IReadOnlyList<LibraryPlaylist> VisibleLibrary => IsLibraryExpanded ? Library : Library.Take(CollapsedLibraryCount).ToList();
    public bool CanExpandLibrary => Library.Count > CollapsedLibraryCount;
    public string LibraryToggleText => IsLibraryExpanded ? Loc.Get("LibraryShowLess") : Loc.Format("LibraryShowAll", Library.Count);

    [RelayCommand]
    private void ToggleLibrary() => IsLibraryExpanded = !IsLibraryExpanded;

    [RelayCommand]
    private Task RefreshLibrary() => LoadLibraryAsync();

    private async Task LoadLibraryAsync()
    {
        libraryCts?.Cancel();
        Library = [];
        LibraryError = null;
        IsLibraryLoading = false;
        if (!YoutubeCookies.HasCookies)
            return;

        var cts = libraryCts = new CancellationTokenSource();
        IsLibraryLoading = true;
        try
        {
            Library = await YoutubeLibrary.GetPlaylistsAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            await GlobalConsts.Log(ex.ToString(), "LoadLibrary");
            LibraryError = Loc.Get("LibraryLoadFailed");
        }

        IsLibraryLoading = false;
    }

    [RelayCommand]
    private async Task OpenLibraryPlaylist(LibraryPlaylist playlist)
    {
        if (playlist == null)
            return;

        LinkText = playlist.Link;
        if (CanLoad)
            await Load();
    }

    #endregion

    #region Link input

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsLoading), nameof(IsLoaded))]
    private string state = StateEmpty;

    public bool IsEmpty => State == StateEmpty;
    public bool IsLoading => State == StateLoading;
    public bool IsLoaded => State == StateLoaded;

    [ObservableProperty]
    private string linkText = string.Empty;

    [ObservableProperty] private string detectionText;
    [ObservableProperty] private bool detectionOk;
    [ObservableProperty] private bool detectionBad;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private bool canLoad;

    [ObservableProperty] private string clipboardLink;
    [ObservableProperty] private bool showClipboardBanner;
    [ObservableProperty] private string loadError;

    /// <summary>The last load failed because YouTube is rate-limiting anonymous requests.</summary>
    [ObservableProperty] private bool isRateLimited;

    partial void OnLinkTextChanged(string value) => UpdateDetection();

    partial void OnLoadErrorChanged(string value)
    {
        if (value == null)
            IsRateLimited = false;
    }

    [RelayCommand]
    private async Task SignIn()
    {
        // Signed in: try the same links again right away.
        if (await main.Settings.SignInAsync() && LoadCommand.CanExecute(null))
            await LoadCommand.ExecuteAsync(null);
    }

    private void UpdateDetection()
    {
        var lines = LinkResolver.SplitLines(LinkText);
        LoadError = null;

        if (lines.Length == 0)
        {
            DetectionText = null;
            DetectionOk = DetectionBad = CanLoad = false;
            return;
        }

        var kinds = lines.Select(LinkResolver.Detect).ToList();
        if (kinds.Any(k => k == LinkKind.None))
        {
            DetectionText = Loc.Get(lines.Length > 1 ? "OneLineInvalid" : "NotAYoutubeLink");
            DetectionOk = false;
            DetectionBad = true;
            CanLoad = false;
            return;
        }

        DetectionText = lines.Length > 1
            ? Loc.Format("DetectedMany", lines.Length)
            : Loc.Get(kinds[0] switch
            {
                LinkKind.Playlist => "DetectedPlaylist",
                LinkKind.Channel => "DetectedChannel",
                _ => "DetectedVideo"
            });
        DetectionOk = true;
        DetectionBad = false;
        CanLoad = true;
    }

    /// <summary>Called when the window gets focus: offers a YouTube link found on the clipboard.</summary>
    public void CheckClipboard()
    {
        if (!GlobalConsts.settings.DetectClipboard || !IsEmpty)
            return;

        string text = null;
        try
        {
            if (Clipboard.ContainsText())
                text = Clipboard.GetText()?.Trim();
        }
        catch
        {
            // The clipboard can be locked by another app; just skip this time.
        }

        var link = LinkResolver.SplitLines(text).FirstOrDefault();
        if (link == null || link.Length > 500 || LinkResolver.Detect(link) == LinkKind.None || link == dismissedClipboardLink || LinkResolver.SplitLines(LinkText).Contains(link))
        {
            ShowClipboardBanner = false;
            return;
        }

        ClipboardLink = link;
        ShowClipboardBanner = true;
    }

    [RelayCommand]
    private async Task UseClipboard()
    {
        LinkText = ClipboardLink;
        dismissedClipboardLink = ClipboardLink;
        ShowClipboardBanner = false;
        if (CanLoad)
            await Load();
    }

    [RelayCommand]
    private void DismissClipboard()
    {
        dismissedClipboardLink = ClipboardLink;
        ShowClipboardBanner = false;
    }

    [RelayCommand]
    private void Paste()
    {
        try
        {
            if (Clipboard.ContainsText())
                LinkText = Clipboard.GetText().Trim();
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void Clear() => LinkText = string.Empty;

    /// <summary>Text dropped from a browser; appended as a new line when there is already a link.</summary>
    public void AcceptDroppedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        LinkText = string.IsNullOrWhiteSpace(LinkText) ? text.Trim() : $"{LinkText.TrimEnd()}{Environment.NewLine}{text.Trim()}";
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task Load()
    {
        var lines = LinkResolver.SplitLines(LinkText).Distinct().ToArray();
        if (lines.Length == 0)
            return;

        loadCts?.Cancel();
        loadCts = new CancellationTokenSource();
        var token = loadCts.Token;

        State = StateLoading;
        ShowClipboardBanner = false;

        var sources = new List<ResolvedSource>();
        var failures = new List<(string Link, string Reason)>();
        var rateLimited = false;

        foreach (var line in lines)
        {
            try
            {
                sources.Add(await LinkResolver.ResolveAsync(line, token));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await GlobalConsts.Log(ex.ToString(), $"Resolve {line}");
                failures.Add((line, GlobalConsts.FriendlyError(ex)));
                rateLimited |= ex is YoutubeExplode.Exceptions.RequestLimitExceededException;
            }
        }

        if (token.IsCancellationRequested)
            return;

        if (sources.Count == 0)
        {
            State = StateEmpty;
            LoadError = failures.Count > 0 ? $"{Loc.Get("LoadFailed")}. {failures[0].Reason}" : Loc.Get("LoadFailed");
            DetectionOk = false;
            IsRateLimited = rateLimited;
            return;
        }

        ShowSources(sources);
        State = StateLoaded;

        if (failures.Count > 0)
            await main.ShowFailuresAsync(failures, retry: null);
    }

    [RelayCommand]
    private void ChangeLink()
    {
        loadCts?.Cancel();
        State = StateEmpty;
    }

    #endregion

    #region Loaded source

    private List<ResolvedSource> sources = [];

    public ObservableCollection<VideoItemViewModel> Videos { get; } = [];
    public ICollectionView VideosView { get; }

    [ObservableProperty] private string sourceTitle;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceAuthorSeparator))]
    private string sourceAuthor;

    /// <summary>The account's automatic playlists (liked videos, liked music) have no author to separate from the counts.</summary>
    public string SourceAuthorSeparator => string.IsNullOrWhiteSpace(SourceAuthor) ? string.Empty : "  ·  ";
    [ObservableProperty] private string sourceKindText;
    [ObservableProperty] private string sourceMeta;
    [ObservableProperty] private string sourceThumbnail;
    [ObservableProperty] private string foundText;
    [ObservableProperty] private string loadedLink;
    [ObservableProperty] private string sourceKindIcon = "I.List";

    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private int existingCount;
    [ObservableProperty] private string selectionText;
    [ObservableProperty] private string dockCountText;
    [ObservableProperty] private string dockSubText;

    [ObservableProperty]
    private string listFilter = "all";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isGridLayout = true;

    public int TotalCount => Videos.Count;

    private bool updatingSelectAll;

    /// <summary>true = all, false = none, null = some.</summary>
    public bool? SelectAllState
    {
        get
        {
            if (SelectedCount == 0)
                return false;

            return SelectedCount == Videos.Count ? true : null;
        }
        set
        {
            if (updatingSelectAll)
                return;

            // Clicking the three-state box while partially selected selects everything.
            var select = value != false;
            updatingSelectAll = true;
            foreach (var video in Videos)
                video.IsSelected = select;
            updatingSelectAll = false;
            UpdateSelection();
        }
    }

    partial void OnListFilterChanged(string value) => VideosView.Refresh();
    partial void OnSearchTextChanged(string value) => VideosView.Refresh();

    private bool FilterVideo(object obj)
    {
        if (obj is not VideoItemViewModel video)
            return false;

        if (!string.IsNullOrWhiteSpace(SearchText) && !video.Title.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase))
            return false;

        return ListFilter switch
        {
            "sel" => video.IsSelected,
            "exists" => video.Exists,
            _ => true
        };
    }

    private void ShowSources(List<ResolvedSource> resolved)
    {
        sources = resolved;
        Videos.Clear();

        foreach (var source in resolved)
        {
            var index = 0;
            foreach (var video in source.Videos)
                Videos.Add(new VideoItemViewModel(video, index++, source.Playlist, OnVideoSelectionChanged));
        }

        ListFilter = "all";
        SearchText = string.Empty;
        LoadedLink = string.Join("  ·  ", resolved.Select(x => x.Link));
        RefreshExisting();
        RefreshTexts();
        VideosView.Refresh();
    }

    private void OnVideoSelectionChanged()
    {
        if (!updatingSelectAll)
            UpdateSelection();
    }

    private void UpdateSelection()
    {
        SelectedCount = Videos.Count(x => x.IsSelected);
        OnPropertyChanged(nameof(SelectAllState));
        OnPropertyChanged(nameof(TotalCount));
        UpdateCountTexts();
        DownloadCommand.NotifyCanExecuteChanged();

        if (ListFilter == "sel")
            VideosView.Refresh();
    }

    private void UpdateCountTexts()
    {
        SelectionText = SelectedCount == Videos.Count && Videos.Count > 0
            ? Loc.Get("AllSelected")
            : Loc.Format("SelectedOfTotal", SelectedCount, Videos.Count);

        DockCountText = Loc.Format(SelectedCount == 1 ? "OneItemSelected" : "ItemsSelected", SelectedCount);

        var duration = TimeSpan.FromTicks(Videos.Where(x => x.IsSelected).Sum(x => x.Video.Duration?.Ticks ?? 0));
        DockSubText = SelectedCount == 0 ? Loc.Get("NothingSelected") : Loc.Format("TotalDuration", Format.LongDuration(duration));
    }

    private void RefreshTexts()
    {
        UpdateDetection();

        if (sources.Count == 0)
            return;

        if (sources.Count == 1)
        {
            var source = sources[0];
            SourceTitle = source.Title;
            SourceAuthor = source.Author;
            SourceThumbnail = source.ThumbnailUrl ?? Videos.FirstOrDefault()?.ThumbnailUrl;
            (SourceKindText, SourceKindIcon, FoundText) = source.Kind switch
            {
                LinkKind.Playlist => (Loc.Get("Playlist"), "I.List", Loc.Get("PlaylistFound")),
                LinkKind.Channel => (Loc.Get("TypeChannel"), "I.User", Loc.Get("ChannelFound")),
                _ => (Loc.Get("Video"), "I.Film", Loc.Get("VideoFound"))
            };
        }
        else
        {
            SourceTitle = Loc.Format("LinksCount", sources.Count);
            SourceAuthor = string.Join(", ", sources.Select(x => x.Author).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(3));
            SourceThumbnail = Videos.FirstOrDefault()?.ThumbnailUrl;
            SourceKindText = Loc.Get("TypeMany");
            SourceKindIcon = "I.Stack";
            FoundText = Loc.Get("LinksFound");
        }

        var total = TimeSpan.FromTicks(Videos.Sum(x => x.Video.Duration?.Ticks ?? 0));
        var count = Videos.Count == 1 ? Loc.Get("OneVideo") : Loc.Format("VideosCount", Videos.Count);
        SourceMeta = total > TimeSpan.Zero ? $"{count} · {Format.LongDuration(total)}" : count;

        UpdateSelection();
        RebuildOptions();
    }

    /// <summary>Marks videos whose output file already exists with the current name pattern and format.</summary>
    private void RefreshExisting()
    {
        var settings = BuildSettings();
        var extension = settings.AudioOnly ? null : settings.Convert ? settings.SaveFormat : settings.VideoSaveFormat;

        foreach (var video in Videos)
        {
            try
            {
                var directory = DestinationFor(video.Playlist, settings);
                var name = GlobalConsts.CleanFileName(settings.GetFilenameByPattern(video.Video, video.Index, video.Video.Title, video.Playlist)).Trim();
                video.Exists = extension != null
                    ? File.Exists(Path.Combine(directory, $"{name}.{extension}"))
                    : Directory.Exists(directory) && Directory.EnumerateFiles(directory, $"{name}.*").Any();
            }
            catch
            {
                video.Exists = false;
            }
        }

        ExistingCount = Videos.Count(x => x.Exists);
        if (ListFilter == "exists")
            VideosView.Refresh();
    }

    #endregion

    #region Download options

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAudioMode), nameof(IsVideoMode))]
    private string mode = "video";

    public bool IsAudioMode => Mode == "audio";
    public bool IsVideoMode => Mode != "audio";

    [ObservableProperty] private IReadOnlyList<Option> qualityOptions = [];
    [ObservableProperty] private Option selectedQuality;
    [ObservableProperty] private IReadOnlyList<Option> formatOptions = [];
    [ObservableProperty] private Option selectedFormat;
    [ObservableProperty] private bool qualityEnabled = true;
    [ObservableProperty] private string saveDirectory;
    [ObservableProperty] private bool isMoreOpen;

    [ObservableProperty] private bool savePlaylistsInDifferentDirectories;
    [ObservableProperty] private bool downloadCaptions;
    [ObservableProperty] private bool tagAudioFile;
    [ObservableProperty] private bool skipExisting;

    public void LoadFromSettings()
    {
        suppressSettingsWrite = true;
        var settings = GlobalConsts.DownloadSettings;
        Mode = settings.AudioOnly || settings.Convert ? "audio" : "video";
        SaveDirectory = GlobalConsts.settings.SaveDirectory;
        SavePlaylistsInDifferentDirectories = settings.SavePlaylistsInDifferentDirectories;
        DownloadCaptions = settings.DownloadCaptions;
        TagAudioFile = settings.TagAudioFile;
        SkipExisting = settings.SkipExisting;
        RebuildOptions();
        suppressSettingsWrite = false;
    }

    private void RebuildOptions()
    {
        var wasSuppressed = suppressSettingsWrite;
        suppressSettingsWrite = true;
        var settings = GlobalConsts.DownloadSettings;

        if (IsAudioMode)
        {
            FormatOptions = [new Option(OriginalKey, Loc.Get("OriginalAudio")), .. AudioFormats.Select(x => new Option(x, x.ToUpperInvariant()))];
            SelectedFormat = settings.AudioOnly ? FormatOptions[0] : FormatOptions.FirstOrDefault(x => x.Key == settings.SaveFormat) ?? FormatOptions[1];

            QualityOptions = [new Option(OriginalKey, Loc.Get("OriginalBitrate")), .. Bitrates.Select(x => new Option(x, $"{x} kbps"))];
            SelectedQuality = settings.SetBitrate ? QualityOptions.FirstOrDefault(x => x.Key == settings.Bitrate) ?? QualityOptions[0] : QualityOptions[0];
            QualityEnabled = SelectedFormat.Key != OriginalKey;
        }
        else
        {
            FormatOptions = VideoFormats.Select(x => new Option(x, x.ToUpperInvariant())).ToList();
            SelectedFormat = FormatOptions.FirstOrDefault(x => x.Key == settings.VideoSaveFormat) ?? FormatOptions[0];

            QualityOptions = [new Option(BestKey, Loc.Get("BestQuality")), .. Resolutions.Select(x => new Option(x, x))];
            SelectedQuality = settings.UseBestQuality ? QualityOptions[0] : QualityOptions.FirstOrDefault(x => x.Key == settings.Quality.Label) ?? QualityOptions[0];
            QualityEnabled = true;
        }

        suppressSettingsWrite = wasSuppressed;
    }

    partial void OnModeChanged(string value)
    {
        if (suppressSettingsWrite)
            return;

        var settings = GlobalConsts.DownloadSettings;
        if (value == "audio")
        {
            settings.Convert = !settings.AudioOnly;
        }
        else
        {
            settings.AudioOnly = false;
            settings.Convert = false;
        }

        RebuildOptions();
        SaveOptions();
    }

    partial void OnSelectedFormatChanged(Option value)
    {
        if (suppressSettingsWrite || value == null)
            return;

        var settings = GlobalConsts.DownloadSettings;
        if (IsAudioMode)
        {
            settings.AudioOnly = value.Key == OriginalKey;
            settings.Convert = !settings.AudioOnly;
            if (!settings.AudioOnly)
                settings.SaveFormat = value.Key;

            QualityEnabled = !settings.AudioOnly;
        }
        else
        {
            settings.VideoSaveFormat = value.Key;
        }

        SaveOptions();
    }

    partial void OnSelectedQualityChanged(Option value)
    {
        if (suppressSettingsWrite || value == null)
            return;

        var settings = GlobalConsts.DownloadSettings;
        if (IsAudioMode)
        {
            settings.SetBitrate = value.Key != OriginalKey;
            if (settings.SetBitrate)
                settings.Bitrate = value.Key;
        }
        else
        {
            settings.UseBestQuality = value.Key == BestKey;
            if (!settings.UseBestQuality)
                settings.Quality = YoutubeHelpers.FromLabel(value.Key, 30);
        }

        SaveOptions();
    }

    partial void OnSavePlaylistsInDifferentDirectoriesChanged(bool value) => WriteSwitch(s => s.SavePlaylistsInDifferentDirectories = value);
    partial void OnDownloadCaptionsChanged(bool value) => WriteSwitch(s => s.DownloadCaptions = value);
    partial void OnTagAudioFileChanged(bool value) => WriteSwitch(s => s.TagAudioFile = value);
    partial void OnSkipExistingChanged(bool value) => WriteSwitch(s => s.SkipExisting = value);

    private void WriteSwitch(Action<DownloadSettings> write)
    {
        if (suppressSettingsWrite)
            return;

        write(GlobalConsts.DownloadSettings);
        SaveOptions();
    }

    private void SaveOptions()
    {
        GlobalConsts.SaveDownloadSettings();
        RefreshExisting();
    }

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
            RefreshExisting();
        }
    }

    [RelayCommand]
    private void ToggleMore() => IsMoreOpen = !IsMoreOpen;

    private static DownloadSettings BuildSettings() => GlobalConsts.DownloadSettings.Clone();

    private static string DestinationFor(FullPlaylist playlist, DownloadSettings settings)
    {
        var directory = GlobalConsts.settings.SaveDirectory;
        var title = playlist?.Title ?? playlist?.BasePlaylist?.Title;

        if (settings.SavePlaylistsInDifferentDirectories && !string.IsNullOrWhiteSpace(title))
            directory = Path.Combine(directory, GlobalConsts.CleanFileName(title));

        return directory;
    }

    private bool CanDownload() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task Download()
    {
        var settings = BuildSettings();
        var needsFfmpeg = !settings.AudioOnly;

        if (needsFfmpeg && !GlobalConsts.FFmpegExists && !await main.InstallFfmpegAsync())
            return;

        var items = Videos
            .Where(x => x.IsSelected)
            .Select(x => new DownloadItem(x.Video, x.Index, x.Playlist, settings, DestinationFor(x.Playlist, settings)))
            .ToList();

        DownloadQueue.Instance.Enqueue(items);

        Videos.Clear();
        sources = [];
        LinkText = string.Empty;
        State = StateEmpty;

        main.ShowSnack(Loc.Format(items.Count == 1 ? "OneAddedToDownloads" : "AddedToDownloads", items.Count), Loc.Get("View"), () => main.Navigate(MainViewModel.PageDownloads));
    }

    #endregion
}
