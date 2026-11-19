namespace OmniControl.App.Features.Hotkeys;

/// What one hotkey box edits: one key combination and the box's listening state (the keys held while it listens, a refused press).
/// <see cref="HotkeyAction"/> is one (its first key); a further key of an action in the Actions and hotkeys window is a
/// <see cref="HotkeyKeySlot"/>. <see cref="HotkeyCapture"/> and the shared <c>HotkeyBox</c> work on this, never on what owns the key.
public abstract partial class HotkeyTarget : ObservableObject
{
    /// Like "Ctrl+Alt+Up". Empty means not bound.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayText))] private string _gesture = string.Empty;

    /// True while the hotkey box waits for the user to press the new key combination.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyHint))]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private bool _isListening;

    /// The keys held right now while listening ("Ctrl + Alt + ..."), shown in the box before the shortcut is saved.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayText))] private string _preview = string.Empty;

    /// The last key press had no Ctrl, Alt or Win, so it was refused.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyHint))]
    private bool _rejectedNote;

    /// The combination waiting for its main key to be released (it is saved then); null while none is held.
    internal string? PendingGesture { get; set; }

    /// The main key (a key code) the pending combination waits on.
    internal int? PendingKey { get; set; }

    /// What the hotkey box shows: the live preview while listening, the saved shortcut otherwise.
    public string DisplayText => IsListening ? Preview : Gesture;

    public string HotkeyHint => IsListening ? (RejectedNote ? "Add Ctrl or Alt" : "Press keys…") : "Double-click to set";

    /// The box captured a whole combination: it becomes <see cref="Gesture"/> unless the owner of the key decides otherwise (a conflict).
    public virtual void Capture(string gesture) => Gesture = gesture;

    partial void OnIsListeningChanged(bool value)
    {
        RejectedNote = false;
        Preview = string.Empty;
        PendingGesture = null;
        PendingKey = null;
    }

    partial void OnGestureChanged(string value) => GestureSet();

    /// Called after <see cref="Gesture"/> changed.
    protected virtual void GestureSet() { }
}
