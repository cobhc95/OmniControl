using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages.Easy;

/// "Restore defaults" on the Easy page: one question, <see cref="AudioDefaults"/> does the work, and the confirmation line offers "Undo" for a while.
public sealed partial class EasyViewModel
{
    /// The button is on while something differs from the defaults; otherwise its tooltip says why it is grey.
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RestoreDefaultsCommand)), NotifyPropertyChangedFor(nameof(RestoreTip))]
    private bool _canRestore;

    public string RestoreTip => CanRestore ? EasyRestore.Tip : EasyRestore.AtDefaults;

    /// "Undo" is shown beside the confirmation line for <see cref="Timing.RestoreUndo"/> after a restore.
    [ObservableProperty] private bool _canUndoRestore;

    private void RefreshRestore() => CanRestore = !AudioDefaults.IsAtDefaults(_mixer.Settings);

    /// The button and the hotkey: ask once, then restore; nothing changes if the question is declined.
    [RelayCommand(CanExecute = nameof(CanRestore))]
    public async Task RestoreDefaultsAsync()
    {
        if (!CanRestore)
        {
            Confirm(EasyRestore.AtDefaults + ".");
            return;
        }
        if (!await _flowServices.Confirm(EasyRestore.Question)) return;
        AudioDefaults.Restore(_defaults, _snapshot);
        Refresh();
        Confirm(EasyRestore.Done, Timing.RestoreUndo);
        CanUndoRestore = true;
    }

    [RelayCommand]
    private void UndoRestore()
    {
        Confirm(AudioDefaults.Undo(_defaults, _snapshot) ? EasyRestore.Undone : EasyRestore.NoCopy);
        Refresh();
    }
}
