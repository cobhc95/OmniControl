using System.ComponentModel;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// The top of the settings pane in the Actions and hotkeys window for one master action: its keys, one shared hotkey box each
/// (<see cref="HotkeyKeySlot"/>), "+ add key", "Suggest", its devices when it has some, then "Run now" and "Show on its page". Keys stay
/// unique in the set in use: a key another action has is shown with "Also on: ..." and "Move here", never given to both; a reserved key is
/// refused with one sentence (<see cref="Refusal"/>). Every change goes through the action, so the master list saves and rebinds it.
public sealed partial class ActionHotkeys : ObservableObject, IDisposable
{
    private readonly HotkeyRegistry _registry;
    private readonly Func<Avalonia.Visual, HotkeyAction, Task> _changeIcon;

    public HotkeyAction Action { get; }

    public ObservableCollection<HotkeyKeySlot> Slots { get; } = new();

    /// The direction this block is for when the action is one of a family ("Up", "Next", "Toggle"); null for an action on its own.
    public string? Role { get; set; }

    /// What the key boxes are labelled: the direction, else "Hotkey".
    public string Label => Role ?? "Hotkey";

    /// True for the block that also shows what the whole family shares (devices, its page, its icon); every block of a family but the first is not.
    public bool IsLead { get; private set; } = true;

    private readonly List<HotkeyAction> _sharing = new();

    /// Why the last key pressed was refused (reserved, or not usable as a shortcut); empty when nothing was.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasRefusal))] private string _refusal = string.Empty;

    public bool HasRefusal => Refusal.Length > 0;

    /// The key just given to the action: its box takes the keys again once the boxes are rebuilt; null otherwise.
    public string? KeyToFocus { get; set; }

    /// The action has a suggested key it does not have yet.
    public bool CanSuggest => Action.SuggestedKey is { } key && !Action.Keys.Contains(key);

    public string SuggestTip => $"Use the suggested key, {Action.SuggestedKey}";

    public IRelayCommand AddKeyCommand { get; }
    public IRelayCommand SuggestCommand { get; }
    public IRelayCommand RunCommand { get; }
    public IRelayCommand ShowCommand { get; }

    /// <param name="showPage">Opens the sidebar page with this title (the shell owns navigation).</param>
    /// <param name="changeIcon">The shared icon picker for the action ("Change icon…"), over the window holding the visual.</param>
    public ActionHotkeys(HotkeyAction action, HotkeyRegistry registry, Action<string> showPage, Func<Avalonia.Visual, HotkeyAction, Task> changeIcon)
    {
        Action = action;
        _registry = registry;
        _changeIcon = changeIcon;
        AddKeyCommand = new RelayCommand(AddKey);
        SuggestCommand = new RelayCommand(Suggest);
        // Through the registry, so the one gate (block rules) and the UI-thread hand-over apply as for any other way of running it.
        RunCommand = new RelayCommand(() => registry.Run(action.Id));
        ShowCommand = new RelayCommand(() => showPage(action.Category));
        action.PropertyChanged += OnActionChanged;
        action.RefreshDevices(); // the devices plugged in now
        Rebuild();
    }

    /// This block shows the devices of the whole family once: a change to them is copied to the other directions' actions
    /// (<paramref name="others"/>, the following blocks), which are then not the lead; nothing is copied until it is changed.
    public void ShareScopeWith(IEnumerable<ActionHotkeys> others)
    {
        foreach (var other in others)
        {
            other.IsLead = false;
            _sharing.Add(other.Action);
        }
        if (_sharing.Count == 0) return;
        Action.Changed += OnScopeChanged;
    }

    private void OnScopeChanged(HotkeyAction changed)
    {
        foreach (var other in _sharing) other.CopyScopeFrom(changed);
    }

    /// "Change icon…": the shared icon picker; the answer is kept as the action's chosen icon.
    public Task ChangeIconAsync(Avalonia.Visual from) => _changeIcon(from, Action);

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HotkeyAction.Keys)) return;
        Rebuild();
        OnPropertyChanged(nameof(CanSuggest));
    }

    private void Rebuild()
    {
        Slots.Clear();
        foreach (var key in Action.Keys) Slots.Add(NewSlot(key));
    }

    private HotkeyKeySlot NewSlot(string key) => new(key, Captured, MoveHere, Cleared);

    /// "+ add key": a new empty box that listens at once.
    private void AddKey()
    {
        if (Slots.Any(s => s.Key.Length == 0)) return; // one new box at a time
        var slot = NewSlot(string.Empty);
        Slots.Add(slot);
        slot.IsListening = true;
    }

    private void Suggest()
    {
        if (Action.SuggestedKey is not { } key || Action.Keys.Contains(key)) return;
        // The key goes through the same rules as a pressed one; the box is only shown when it has to say whose the key is (a transient box
        // added and then replaced by the rebuild a given key causes would change the list under the command that is running).
        var slot = Slots.FirstOrDefault(s => s.Key.Length == 0) ?? NewSlot(string.Empty);
        slot.Capture(key);
        if (slot.HasConflict && !Slots.Contains(slot)) Slots.Add(slot);
    }

    /// A key was captured in a box: given to the action, refused, or shown as another action's.
    private bool Captured(HotkeyKeySlot slot, string gesture)
    {
        Refusal = HotkeyService.ProblemWith(gesture) ?? ReservedKeys.ProblemWith(gesture) ?? string.Empty;
        if (HasRefusal)
        {
            slot.Quietly(slot.Key);
            return false;
        }
        if (_registry.OwnerOf(gesture, Action.Id) is { } other)
        {
            slot.Conflict = $"Also on: {other.Title}";
            return false;
        }
        Give(slot, gesture);
        return true;
    }

    /// "Move here": the key leaves the other action (the master list keeps keys unique) and joins this one.
    private void MoveHere(HotkeyKeySlot slot) => Give(slot, slot.Gesture);

    private void Give(HotkeyKeySlot slot, string gesture)
    {
        KeyToFocus = gesture;
        Action.SetKeys(slot.Key.Length == 0 ? Action.Keys.Append(gesture) : Action.Keys.Select(k => k == slot.Key ? gesture : k));
    }

    private void Cleared(HotkeyKeySlot slot)
    {
        Refusal = string.Empty;
        if (slot.Key.Length == 0) Slots.Remove(slot);
        else Action.SetKeys(Action.Keys.Where(k => k != slot.Key));
    }

    public void Dispose()
    {
        Action.PropertyChanged -= OnActionChanged;
        Action.Changed -= OnScopeChanged;
    }
}
