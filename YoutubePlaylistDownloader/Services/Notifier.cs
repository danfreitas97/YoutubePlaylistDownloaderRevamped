namespace YoutubePlaylistDownloader.Services;

/// <summary>
/// Shows a Windows notification through a short-lived tray icon.
/// </summary>
public static class Notifier
{
    private static System.Windows.Forms.NotifyIcon icon;

    public static void Show(string title, string text, Action onClick = null)
    {
        try
        {
            icon ??= new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath),
                Text = Loc.Get("Title")
            };

            icon.Visible = true;
            icon.BalloonTipClicked -= OnClicked;
            clickAction = onClick;
            icon.BalloonTipClicked += OnClicked;
            icon.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.None);
        }
        catch (Exception ex)
        {
            GlobalConsts.Log(ex.ToString(), "Notifier.Show").ConfigureAwait(false);
        }
    }

    private static Action clickAction;

    private static void OnClicked(object sender, EventArgs e) =>
        Application.Current?.Dispatcher.Invoke(() => clickAction?.Invoke());

    public static void Dispose()
    {
        if (icon == null)
            return;

        icon.Visible = false;
        icon.Dispose();
        icon = null;
    }
}
