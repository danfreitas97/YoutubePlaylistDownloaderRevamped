using System.Windows.Shell;

namespace YoutubePlaylistDownloader.ViewModels;

public partial class LanguageOption(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool isCurrent;
}

/// <summary>
/// The window: navigation, language and theme switching, the snackbar, dialogs and app-wide notices.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public const string PageDownload = "download";
    public const string PageDownloads = "downloads";
    public const string PageSettings = "settings";

    private readonly DispatcherTimer snackTimer;
    private Action snackAction;

    public MainViewModel()
    {
        Dialog = new DialogViewModel();
        Download = new DownloadViewModel(this);
        Downloads = new DownloadsViewModel(this);
        Settings = new SettingsViewModel(this);

        Languages = Loc.Languages.Select(x => new LanguageOption(x)).ToList();
        UpdateLanguageState();

        snackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        snackTimer.Tick += (s, e) => HideSnack();

        DownloadQueue.Instance.Updated += (s, e) => UpdateQueueState();
        DownloadQueue.Instance.Drained += OnQueueDrained;
        Loc.LanguageChanged += (s, e) => UpdateLanguageState();
        ThemeService.ThemeChanged += (s, e) => OnPropertyChanged(nameof(IsDark));
    }

    public DialogViewModel Dialog { get; }
    public DownloadViewModel Download { get; }
    public DownloadsViewModel Downloads { get; }
    public SettingsViewModel Settings { get; }

    #region Navigation

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadPage), nameof(IsDownloadsPage), nameof(IsSettingsPage))]
    private string page = PageDownload;

    public bool IsDownloadPage => Page == PageDownload;
    public bool IsDownloadsPage => Page == PageDownloads;
    public bool IsSettingsPage => Page == PageSettings;

    partial void OnPageChanged(string value)
    {
        if (value == PageDownload)
            Download.LoadFromSettings();
        else if (value == PageSettings)
            Settings.Refresh();
    }

    [RelayCommand]
    public void Navigate(string page) => Page = page;

    #endregion

    #region Language & theme

    public IReadOnlyList<LanguageOption> Languages { get; }

    [ObservableProperty] private string currentLanguage;
    [ObservableProperty] private bool isLanguageMenuOpen;
    [ObservableProperty] private FlowDirection flowDirection = FlowDirection.LeftToRight;

    public bool IsDark => ThemeService.IsDark;

    private void UpdateLanguageState()
    {
        CurrentLanguage = Loc.Current;
        FlowDirection = Loc.FlowDirection;
        foreach (var language in Languages)
            language.IsCurrent = language.Name == CurrentLanguage;

        foreach (var item in DownloadQueue.Instance.Items)
            item.RefreshTexts();

        UpdateQueueState();
    }

    [RelayCommand]
    private void ToggleLanguageMenu() => IsLanguageMenuOpen = !IsLanguageMenuOpen;

    [RelayCommand]
    private void ChangeLanguage(string language)
    {
        IsLanguageMenuOpen = false;
        if (language != Loc.Current)
            Loc.Apply(language);
    }

    [RelayCommand]
    private void ToggleTheme() => ThemeService.Toggle();

    #endregion

    #region Queue state (rail badge, taskbar)

    [ObservableProperty] private string activeBadge;
    [ObservableProperty] private double overallProgress;
    [ObservableProperty] private TaskbarItemProgressState taskbarState = TaskbarItemProgressState.None;
    [ObservableProperty] private double taskbarProgress;

    private void UpdateQueueState()
    {
        var queue = DownloadQueue.Instance;
        var active = queue.ActiveCount;
        ActiveBadge = active > 0 ? (active > 99 ? "99+" : active.ToString()) : null;

        var running = queue.Items.Any(x => x.Status is DownloadStatus.Downloading or DownloadStatus.Converting);
        OverallProgress = active > 0 ? Math.Max(running ? 2 : 0, queue.OverallProgress) : 0;
        TaskbarState = running ? TaskbarItemProgressState.Normal : active > 0 ? TaskbarItemProgressState.Paused : TaskbarItemProgressState.None;
        TaskbarProgress = OverallProgress / 100;

        Downloads.Refresh();
    }

    private void OnQueueDrained(object sender, int finished)
    {
        if (GlobalConsts.DownloadSettings.OpenDestinationFolderWhenDone)
            OpenFolder(GlobalConsts.settings.SaveDirectory);

        if (GlobalConsts.settings.NotifyWhenDone && Application.Current.MainWindow is { IsActive: false })
            Notifier.Show(Loc.Get("DownloadsFinished"), Loc.Format(finished == 1 ? "OneFileSaved" : "FilesSaved", finished), () =>
            {
                Navigate(PageDownloads);
                Application.Current.MainWindow?.Activate();
            });
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            GlobalConsts.Log(ex.ToString(), "OpenFolder").ConfigureAwait(false);
        }
    }

    public static void ShowInFolder(string file)
    {
        try
        {
            if (File.Exists(file))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            else
                OpenFolder(Path.GetDirectoryName(file));
        }
        catch (Exception ex)
        {
            GlobalConsts.Log(ex.ToString(), "ShowInFolder").ConfigureAwait(false);
        }
    }

    #endregion

    #region Snackbar

    [ObservableProperty] private bool isSnackVisible;
    [ObservableProperty] private string snackText;
    [ObservableProperty] private string snackActionText;

    public void ShowSnack(string text, string actionText = null, Action action = null)
    {
        SnackText = text;
        SnackActionText = actionText;
        snackAction = action;
        IsSnackVisible = true;
        snackTimer.Stop();
        snackTimer.Start();
    }

    [RelayCommand]
    private void SnackAct()
    {
        HideSnack();
        snackAction?.Invoke();
    }

    private void HideSnack()
    {
        snackTimer.Stop();
        IsSnackVisible = false;
    }

    #endregion

    #region Dialogs

    /// <summary>The "some videos could not be loaded/downloaded" dialog, with copy, save and retry.</summary>
    public async Task ShowFailuresAsync(IReadOnlyList<(string Item, string Reason)> failures, Action retry)
    {
        var text = string.Join(Environment.NewLine, failures.Select(x => $"{x.Item} — {x.Reason}"));
        var buttons = new List<DialogButton>
        {
            new(Loc.Get("CopyToClipboard"), "copy"),
            new(Loc.Get("SaveToTextFile"), "save")
        };

        if (retry != null)
            buttons.Add(new DialogButton(Loc.Get("Retry"), "retry"));

        buttons.Add(new DialogButton(Loc.Get("Ok"), "ok", true));

        while (true)
        {
            var result = await Dialog.ShowAsync(Loc.Get("CouldntDownload"), $"{Loc.Get("ListOfNotDownloadedVideos")}{Environment.NewLine}{Environment.NewLine}{text}", [.. buttons]);

            if (result == "copy")
            {
                try { Clipboard.SetText(text); } catch { }
                ShowSnack(Loc.Get("Copied"));
                continue;
            }

            if (result == "save")
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "Videos not downloaded", DefaultExt = ".txt", Filter = "Text documents (.txt)|*.txt" };
                if (dialog.ShowDialog() == true)
                    File.WriteAllText(dialog.FileName, text);
                continue;
            }

            if (result == "retry")
                retry?.Invoke();

            break;
        }
    }

    #endregion

    #region Startup, updates and exit

    public async Task OnStartedAsync()
    {
        Download.CheckClipboard();
        CookieRefresher.Start();

        if (GlobalConsts.settings.CheckForProgramUpdates)
            await CheckForUpdatesAsync(silent: true);
    }

    public async Task CheckForUpdatesAsync(bool silent)
    {
        UpdateInfo update;
        try
        {
            update = await UpdateService.CheckAsync();
        }
        catch (Exception ex)
        {
            await GlobalConsts.Log(ex.ToString(), "CheckForUpdates");
            if (!silent)
                await Dialog.ShowMessageAsync(Loc.Get("Error"), $"{Loc.Get("CannotUpdate")} {ex.Message}");
            return;
        }

        if (update == null)
        {
            if (!silent)
                await Dialog.ShowMessageAsync(Loc.Get("NoUpdates"), Loc.Get("NoUpdatesAvailable"));
            return;
        }

        var choice = await Dialog.ShowAsync(Loc.Get("NewVersionAvailable"), $"{Loc.Get("DoYouWantToUpdate")}{Environment.NewLine}{update.Changelog}",
            new DialogButton(Loc.Get("No"), "no"),
            new DialogButton(Loc.Get("UpdateWhenIExit"), "later"),
            new DialogButton(Loc.Get("UpdateNow"), "now", true));

        if (choice == "no" || choice == "cancel")
            return;

        if (choice == "later")
        {
            UpdateService.RunSetupOnExit = true;
            _ = DownloadUpdateQuietlyAsync(update);
            return;
        }

        await DownloadUpdateWithProgressAsync(update);
    }

    private async Task DownloadUpdateQuietlyAsync(UpdateInfo update)
    {
        try
        {
            await UpdateService.DownloadAsync(update, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            UpdateService.RunSetupOnExit = false;
            await GlobalConsts.Log(ex.ToString(), "DownloadUpdateQuietly");
        }
    }

    /// <summary>Explains what ffmpeg is and offers to download it; true once it is available.</summary>
    public async Task<bool> InstallFfmpegAsync()
    {
        var choice = await Dialog.ShowAsync(Loc.Get("FfmpegMissingTitle"), Loc.Format("FfmpegMissingMessage", FfmpegService.DownloadSizeMb),
            new DialogButton(Loc.Get("NotNow"), "no"),
            new DialogButton(Loc.Get("DownloadFfmpeg"), "yes", true));
        if (choice != "yes")
            return false;

        using var cts = new CancellationTokenSource();
        var dialogTask = Dialog.ShowAsync(Loc.Get("DownloadingFfmpeg"), Loc.Get("DownloadingFfmpegMessage"), new DialogButton(Loc.Get("Cancel"), "cancel"));
        Dialog.ShowProgress = true;

        var download = FfmpegService.DownloadAsync(new Progress<double>(p => Dialog.Progress = p), cts.Token);
        var finished = await Task.WhenAny(download, dialogTask);

        if (finished == dialogTask)
        {
            cts.Cancel();
            try { await download; } catch { }
            return false;
        }

        try
        {
            await download;
        }
        catch (Exception ex)
        {
            Dialog.ChooseCommand.Execute("done");
            await dialogTask;
            await GlobalConsts.Log(ex.ToString(), "DownloadFfmpeg");
            await Dialog.ShowMessageAsync(Loc.Get("Error"), $"{Loc.Get("FfmpegDownloadFailed")} {GlobalConsts.FriendlyError(ex)}");
            return false;
        }

        Dialog.ChooseCommand.Execute("done");
        await dialogTask;
        return GlobalConsts.FFmpegExists;
    }

    private async Task DownloadUpdateWithProgressAsync(UpdateInfo update)
    {
        using var cts = new CancellationTokenSource();
        var dialogTask = Dialog.ShowAsync(Loc.Get("DownloadingUpdateSetup"), $"YouTube Playlist Downloader {update.Version}", new DialogButton(Loc.Get("Cancel"), "cancel"));
        Dialog.ShowProgress = true;

        var download = UpdateService.DownloadAsync(update, new Progress<double>(p => Dialog.Progress = p), cts.Token);
        var finished = await Task.WhenAny(download, dialogTask);

        if (finished == dialogTask)
        {
            cts.Cancel();
            try { await download; } catch { }
            return;
        }

        try
        {
            await download;
        }
        catch (Exception ex)
        {
            Dialog.ChooseCommand.Execute("done");
            await dialogTask;
            await GlobalConsts.Log(ex.ToString(), "DownloadUpdate");
            await Dialog.ShowMessageAsync(Loc.Get("UpdateFailed"), $"{Loc.Get("CannotUpdate")}{GlobalConsts.FriendlyError(ex)}");
            return;
        }

        Dialog.ChooseCommand.Execute("done");
        await dialogTask;

        UpdateService.RunSetup();
        Application.Current.Shutdown();
    }

    /// <summary>Returns false to keep the window open.</summary>
    public async Task<bool> ConfirmExitAsync()
    {
        var active = DownloadQueue.Instance.ActiveCount;

        if (active > 0)
            return await Dialog.ConfirmAsync(Loc.Get("Exit"), Loc.Format("ExitWhileDownloading", active), Loc.Get("Exit"), Loc.Get("Cancel"));

        if (GlobalConsts.settings.ConfirmExit)
            return await Dialog.ConfirmAsync(Loc.Get("Exit"), Loc.Get("ExitMessage"));

        return true;
    }

    #endregion
}
