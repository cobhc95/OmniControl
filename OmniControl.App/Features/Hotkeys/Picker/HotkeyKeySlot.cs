using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// One key of the highlighted action in the Actions and hotkeys window, edited in its own shared <c>HotkeyBox</c>; a new slot ("+ add key")
/// starts empty and listening. A captured key goes to the owner (<see cref="ActionHotkeys"/>), which gives it to the action, refuses it
/// with one sentence, or shows whose key it is (<see cref="Conflict"/>) with "Move here".
public sealed partial class HotkeyKeySlot : HotkeyTarget
{
    private readonly Func<HotkeyKeySlot, string, bool> _captured;
    private readonly Action<HotkeyKeySlot> _cleared;
    private bool _quiet;

    /// The key this slot stands for in the action (empty for a new slot); what is replaced when another key is captured here.
    public string Key { get; }

    /// "Also on: Snipping Tool" while the captured key belongs to another action and has not been moved here; empty otherwise.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    private string _conflict = string.Empty;

    public bool HasConflict => Conflict.Length > 0;

    public IRelayCommand MoveHereCommand { get; }
    public IRelayCommand RemoveCommand { get; }

    /// <param name="captured">Takes a captured key; false when it was not given to the action (refused or taken), so the slot shows it pending.</param>
    /// <param name="moveHere">Takes the pending key from the action that has it.</param>
    /// <param name="cleared">Removes this slot's key (the remove cross, Backspace or Delete).</param>
    internal HotkeyKeySlot(string key, Func<HotkeyKeySlot, string, bool> captured, Action<HotkeyKeySlot> moveHere, Action<HotkeyKeySlot> cleared)
    {
        Key = key;
        _captured = captured;
        _cleared = cleared;
        Quietly(key);
        MoveHereCommand = new RelayCommand(() => moveHere(this));
        RemoveCommand = new RelayCommand(() => cleared(this));
    }

    public override void Capture(string gesture)
    {
        if (gesture == Key) return;
        Quietly(gesture);
        if (!_captured(this, gesture)) return;
        Conflict = string.Empty;
    }

    /// Shows a key in the box without it counting as an edit.
    internal void Quietly(string gesture)
    {
        _quiet = true;
        Gesture = gesture;
        _quiet = false;
    }

    /// Backspace or Delete in the box empties it: the key is taken off the action.
    protected override void GestureSet()
    {
        if (!_quiet && Gesture.Length == 0) _cleared(this);
    }
}
