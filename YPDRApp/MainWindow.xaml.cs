namespace YoutubePlaylistDownloader;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private bool closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainViewModel();
        DataContext = viewModel;

        SourceInitialized += (s, e) => WindowFrame.Apply(this);
        StateChanged += (s, e) => OnStateChanged();
        Activated += (s, e) => viewModel.Download.CheckClipboard();
        Loaded += async (s, e) => await viewModel.OnStartedAsync();
        Closing += MainWindow_Closing;
        ThemeService.ThemeChanged += (s, e) => WindowFrame.Apply(this);
    }

    private void OnStateChanged()
    {
        // A maximized chrome-less window overhangs the screen by the resize border; pad it back in.
        Root.Margin = WindowState == WindowState.Maximized
            ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left + SystemParameters.FixedFrameVerticalBorderWidth,
                            SystemParameters.WindowResizeBorderThickness.Top + SystemParameters.FixedFrameHorizontalBorderHeight,
                            SystemParameters.WindowResizeBorderThickness.Right + SystemParameters.FixedFrameVerticalBorderWidth,
                            SystemParameters.WindowResizeBorderThickness.Bottom + SystemParameters.FixedFrameHorizontalBorderHeight)
            : new Thickness(0);

        Controls.Ui.SetIcon(MaximizeButton, (System.Windows.Media.Geometry)FindResource(WindowState == WindowState.Maximized ? "I.Restore" : "I.Maximize"));
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void DialogCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private async void MainWindow_Closing(object sender, CancelEventArgs e)
    {
        if (closeConfirmed)
            return;

        e.Cancel = true;
        if (await viewModel.ConfirmExitAsync())
        {
            closeConfirmed = true;
            Close();
        }
    }
}
