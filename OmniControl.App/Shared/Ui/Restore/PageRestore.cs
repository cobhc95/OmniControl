namespace OmniControl.App.Shared.Ui.Restore;

/// The "Restore defaults" button of one page (or of every page) and its calm line: the button, its greyed reason, the line saying what
/// happened and "Undo" for <see cref="Timing.RestoreUndo"/>. The work is <see cref="RestoreRunner"/>'s; this holds what the two shared
/// views (<c>RestoreBar</c>, <c>RestoreLine</c>) show.
public sealed partial class PageRestore : ObservableObject, IDisposable
{
    private readonly RestoreRunner _runner;
    private readonly string _tip;
    private readonly DispatcherTimer _hide = new();
    private bool _busy;

    public PageRestore(RestoreRunner runner, bool all = false)
    {
        _runner = runner;
        _tip = all ? RestoreWords.AllTip : RestoreWords.Tip;
        ButtonText = all ? RestoreWords.AllButtonText : RestoreWords.ButtonText;
        _tooltip = _tip;
        _hide.Tick += OnHide;
        Refresh();
    }

    public string ButtonText { get; }

    /// The button is on while a restore can run; its tooltip says why it is grey otherwise.
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RestoreCommand))] private bool _canRestore;

    [ObservableProperty] private string _tooltip;

    /// The line saying what a restore or an undo did; empty when there is nothing to say.
    [ObservableProperty] private string _confirmation = string.Empty;

    [ObservableProperty] private bool _canUndo;

    /// Looks again at whether the page is at its defaults (the shell asks about once a second while the page is on screen).
    public void Refresh()
    {
        var why = _runner.Blocked;
        CanRestore = why is null && !_busy;
        Tooltip = why ?? _tip;
    }

    /// The button and the page's action: ask once, then restore.
    [RelayCommand(CanExecute = nameof(CanRestore))]
    public async Task RestoreAsync()
    {
        if (_busy) return;
        _busy = true;
        try { Show(await _runner.RunAsync()); }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        _busy = true;
        try { Show(await _runner.UndoAsync()); }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void Show(RestoreResult result)
    {
        if (!result.Ran && result.Line.Length == 0) return; // the question was declined
        Confirmation = result.Line;
        CanUndo = result.CanUndo;
        _hide.Stop();
        _hide.Interval = result.CanUndo ? Timing.RestoreUndo : Timing.RestoreNote;
        _hide.Start();
    }

    private void OnHide(object? sender, EventArgs e)
    {
        _hide.Stop();
        Confirmation = string.Empty;
        CanUndo = false;
    }

    public void Dispose()
    {
        _hide.Stop();
        _hide.Tick -= OnHide;
    }
}
