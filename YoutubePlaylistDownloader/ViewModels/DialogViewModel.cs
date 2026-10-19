namespace YoutubePlaylistDownloader.ViewModels;

public record DialogButton(string Text, string Result, bool IsPrimary = false);

/// <summary>
/// The in-window modal dialog. Only one is shown at a time; later requests wait for the current one.
/// </summary>
public partial class DialogViewModel : ObservableObject
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private TaskCompletionSource<string> pending;

    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private string title;
    [ObservableProperty] private string message;
    [ObservableProperty] private bool showProgress;
    [ObservableProperty] private double progress;
    [ObservableProperty] private IReadOnlyList<DialogButton> buttons = [];

    public async Task<string> ShowAsync(string title, string message, params DialogButton[] buttons)
    {
        await gate.WaitAsync();
        try
        {
            pending = new TaskCompletionSource<string>();
            Title = title;
            Message = message;
            ShowProgress = false;
            Progress = 0;
            Buttons = buttons.Length > 0 ? buttons : [new DialogButton(Loc.Get("Ok"), "ok", true)];
            IsOpen = true;
            return await pending.Task;
        }
        finally
        {
            IsOpen = false;
            gate.Release();
        }
    }

    public Task ShowMessageAsync(string title, string message) => ShowAsync(title, message);

    public async Task<bool> ConfirmAsync(string title, string message, string yes = null, string no = null) =>
        await ShowAsync(title, message,
            new DialogButton(no ?? Loc.Get("No"), "no"),
            new DialogButton(yes ?? Loc.Get("Yes"), "yes", true)) == "yes";

    [RelayCommand]
    private void Choose(string result) => pending?.TrySetResult(result);

    /// <summary>Esc / clicking outside: the same as the first (non-primary) button, or "cancel".</summary>
    [RelayCommand]
    private void Dismiss()
    {
        var fallback = Buttons.FirstOrDefault(x => !x.IsPrimary)?.Result ?? (Buttons.Count == 1 ? Buttons[0].Result : "cancel");
        pending?.TrySetResult(fallback);
    }
}
