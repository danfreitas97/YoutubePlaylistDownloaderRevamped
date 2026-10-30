namespace YoutubePlaylistDownloader.Services;

/// <summary>
/// Holds every download and runs up to <see cref="Objects.Settings.ConcurrentDownloads"/> of them at a time.
/// All collection changes happen on the UI thread.
/// </summary>
public sealed class DownloadQueue
{
    public static DownloadQueue Instance { get; } = new();

    private readonly HashSet<DownloadItem> running = [];
    private readonly DispatcherTimer ticker;
    private int finishedSinceIdle;

    public ObservableCollection<DownloadItem> Items { get; } = [];

    /// <summary>Raised on the UI thread a few times per second while anything is happening, and after every change.</summary>
    public event EventHandler Updated;

    /// <summary>Raised when the queue runs dry after finishing at least one download; the argument is how many finished.</summary>
    public event EventHandler<int> Drained;

    private DownloadQueue()
    {
        ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        ticker.Tick += (s, e) =>
        {
            Updated?.Invoke(this, EventArgs.Empty);
            if (running.Count == 0)
                ticker.Stop();
        };
    }

    public int ActiveCount => Items.Count(x => x.IsActive);

    /// <summary>Average progress of the active items, 0-100.</summary>
    public double OverallProgress
    {
        get
        {
            var active = Items.Where(x => x.IsActive).ToList();
            return active.Count == 0 ? 0 : active.Average(x => x.Progress);
        }
    }

    public void Enqueue(IEnumerable<DownloadItem> items)
    {
        foreach (var item in items)
            Items.Add(item);

        Pump();
    }

    public void Pause(DownloadItem item)
    {
        item.Pause();
        Pump();
    }

    public void Resume(DownloadItem item)
    {
        item.Resume();
        Pump();
    }

    public void Retry(DownloadItem item)
    {
        item.Retry();
        Pump();
    }

    public void Remove(DownloadItem item)
    {
        item.Cancel();
        Items.Remove(item);
        Pump();
    }

    public void PauseAll()
    {
        foreach (var item in Items.Where(x => x.CanPause).ToList())
            item.Pause();

        Pump();
    }

    public void ResumeAll()
    {
        foreach (var item in Items.Where(x => x.CanResume).ToList())
            item.Resume();

        Pump();
    }

    public void RetryFailed()
    {
        foreach (var item in Items.Where(x => x.IsError).ToList())
            item.Retry();

        Pump();
    }

    public void ClearFinished()
    {
        foreach (var item in Items.Where(x => x.IsFinished).ToList())
            Items.Remove(item);

        Pump();
    }

    public void CancelAll()
    {
        foreach (var item in Items)
            item.Cancel();
    }

    /// <summary>Starts queued items while there are free slots.</summary>
    public void Pump()
    {
        var limit = Math.Max(1, GlobalConsts.settings.ConcurrentDownloads);

        while (running.Count < limit)
        {
            var next = Items.FirstOrDefault(x => x.Status == DownloadStatus.Queued && !running.Contains(x));
            if (next == null)
                break;

            Start(next);
        }

        if (running.Count > 0 && !ticker.IsEnabled)
            ticker.Start();

        Updated?.Invoke(this, EventArgs.Empty);
    }

    private void Start(DownloadItem item)
    {
        running.Add(item);
        _ = RunAsync(item);
    }

    private async Task RunAsync(DownloadItem item)
    {
        try
        {
            await Task.Run(item.RunAsync);
        }
        finally
        {
            running.Remove(item);

            // Cancelled while running: the item was already taken out of the list.
            if (item.IsFinished)
                finishedSinceIdle++;

            Pump();

            if (running.Count == 0 && !Items.Any(x => x.Status == DownloadStatus.Queued) && finishedSinceIdle > 0)
            {
                var finished = finishedSinceIdle;
                finishedSinceIdle = 0;
                Drained?.Invoke(this, finished);
            }
        }
    }
}
