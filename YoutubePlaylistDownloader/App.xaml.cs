namespace YoutubePlaylistDownloader;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        GlobalConsts.LoadConsts();
        GlobalConsts.CreateTempFolder();
        ThemeService.Initialize();
        Loc.Initialize();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DownloadQueue.Instance.CancelAll();
        GlobalConsts.SaveConsts();
        Notifier.Dispose();

        if (UpdateService.RunSetupOnExit && !string.IsNullOrWhiteSpace(UpdateService.SetupPath))
            UpdateService.RunSetup();
        else
            GlobalConsts.CleanTempFolder();

        base.OnExit(e);
    }

    private async void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Must be set before the first await: the dispatcher reads it as soon as the handler yields.
#if !DEBUG
        e.Handled = true;
#endif

        await GlobalConsts.Log($"{e.Exception}", "Unhandled exception");

        if (MainWindow?.DataContext is MainViewModel main)
            await main.Dialog.ShowMessageAsync(Loc.Get("Error"), $"{Loc.Get("ErrorMessage")}{Environment.NewLine}{Environment.NewLine}{e.Exception.Message}");
    }
}
