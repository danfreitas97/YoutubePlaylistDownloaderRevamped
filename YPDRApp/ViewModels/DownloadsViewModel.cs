namespace YoutubePlaylistDownloader.ViewModels;

/// <summary>
/// The "Downloads" screen: the queue grouped into in progress / finished, with per-item and bulk actions.
/// </summary>
public partial class DownloadsViewModel : ObservableObject
{
    private readonly MainViewModel main;
    private readonly ListCollectionView view;

    public DownloadsViewModel(MainViewModel main)
    {
        this.main = main;
        view = (ListCollectionView)CollectionViewSource.GetDefaultView(DownloadQueue.Instance.Items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DownloadItem.Section)));
        view.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.Section), ListSortDirection.Ascending));
        view.IsLiveGrouping = true;
        view.IsLiveSorting = true;
        view.IsLiveFiltering = true;
        view.LiveGroupingProperties.Add(nameof(DownloadItem.Section));
        view.LiveSortingProperties.Add(nameof(DownloadItem.Section));
        view.LiveFilteringProperties.Add(nameof(DownloadItem.Status));
        view.Filter = Matches;
        Loc.LanguageChanged += (s, e) => Refresh();
    }

    public ICollectionView Items => view;

    [ObservableProperty]
    private string filter = "all";

    [ObservableProperty] private string summary;
    [ObservableProperty] private bool isEmpty = true;
    [ObservableProperty] private bool hasFinished;
    [ObservableProperty] private bool hasPausable;
    [ObservableProperty] private bool hasResumable;
    [ObservableProperty] private bool hasErrors;

    partial void OnFilterChanged(string value)
    {
        view.Refresh();
        Refresh();
    }

    private bool Matches(object obj) => obj is DownloadItem item && Filter switch
    {
        "active" => item.IsActive,
        "done" => item.IsFinished,
        "error" => item.IsError,
        _ => true
    };

    public void Refresh()
    {
        var items = DownloadQueue.Instance.Items;
        int Count(params DownloadStatus[] statuses) => items.Count(x => statuses.Contains(x.Status));

        var parts = new List<string>();
        void Add(int count, string key)
        {
            if (count > 0)
                parts.Add(Loc.Format(key, count));
        }

        Add(Count(DownloadStatus.Downloading, DownloadStatus.Converting), "SummaryInProgress");
        Add(Count(DownloadStatus.Queued), "SummaryQueued");
        Add(Count(DownloadStatus.Paused), "SummaryPaused");
        Add(Count(DownloadStatus.Done, DownloadStatus.Skipped), "SummaryDone");
        Add(Count(DownloadStatus.Error), "SummaryErrors");

        Summary = string.Join(" · ", parts);
        IsEmpty = view.IsEmpty;
        HasFinished = items.Any(x => x.IsFinished);
        HasPausable = items.Any(x => x.CanPause);
        HasResumable = !HasPausable && items.Any(x => x.CanResume);
        HasErrors = items.Any(x => x.IsError);
    }

    [RelayCommand]
    private void Pause(DownloadItem item) => DownloadQueue.Instance.Pause(item);

    [RelayCommand]
    private void Resume(DownloadItem item) => DownloadQueue.Instance.Resume(item);

    [RelayCommand]
    private void Retry(DownloadItem item) => DownloadQueue.Instance.Retry(item);

    [RelayCommand]
    private void Remove(DownloadItem item) => DownloadQueue.Instance.Remove(item);

    [RelayCommand]
    private void ShowInFolder(DownloadItem item) => MainViewModel.ShowInFolder(item.OutputPath);

    [RelayCommand]
    private void PauseAll() => DownloadQueue.Instance.PauseAll();

    [RelayCommand]
    private void ResumeAll() => DownloadQueue.Instance.ResumeAll();

    [RelayCommand]
    private void RetryFailed() => DownloadQueue.Instance.RetryFailed();

    [RelayCommand]
    private void ClearFinished() => DownloadQueue.Instance.ClearFinished();

    [RelayCommand]
    private void OpenFolder() => MainViewModel.OpenFolder(GlobalConsts.settings.SaveDirectory);

    [RelayCommand]
    private void GoDownload() => main.Navigate(MainViewModel.PageDownload);
}
