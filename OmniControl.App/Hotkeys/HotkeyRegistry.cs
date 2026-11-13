using System.Collections.ObjectModel;
using OmniControl.App.Services;
using Avalonia.Threading;

namespace OmniControl.App.Hotkeys;

/// <summary>
/// The master list of everything OmniControl itself can do. A feature calls <see cref="Register"/> once per action; it then shows up
/// on the Shortcuts page (and in search), in the mouse button and middle-click pickers, and (for a level action) in the wheel gesture
/// picker, and is saved and registered as a hotkey automatically. Consumers read <see cref="Actions"/> and follow its changes.
/// </summary>
public sealed class HotkeyRegistry : IDisposable
{
    private const string FileName = "hotkeys.json";

    private readonly HotkeyService _service = new();
    private readonly Dictionary<string, StoredHotkey> _stored = Load();

    public ObservableCollection<HotkeyAction> Actions { get; } = new();

    /// <summary>Where problems are shown (the footer status line).</summary>
    public Action<string>? Report { get; set; }

    public HotkeyRegistry()
    {
        _service.Failed += gesture => Dispatcher.UIThread.Post(() =>
        {
            var names = string.Join(", ", Actions.Where(a => a.Gesture == gesture).Select(a => a.Title));
            Report?.Invoke($"Couldn't register {gesture} for {names}: another app is probably using it. Try another combination.");
        });
    }

    public void Register(HotkeyAction action)
    {
        if (_stored.TryGetValue(action.Id, out var saved)) action.Restore(saved);
        action.Changed += OnChanged;
        Actions.Add(action);
        Rebind();
    }

    /// <summary>Registers several actions at once (one rebinding of the hotkeys).</summary>
    public void Register(IEnumerable<HotkeyAction> actions)
    {
        foreach (var action in actions)
        {
            if (_stored.TryGetValue(action.Id, out var saved)) action.Restore(saved);
            action.Changed += OnChanged;
            Actions.Add(action);
        }
        Rebind();
    }

    /// <summary>Runs an action by id (for example from a mouse button). False when no such action is registered.</summary>
    public bool Run(string id)
    {
        var action = Actions.FirstOrDefault(a => a.Id == id);
        if (action is null) return false;
        RunSoon(action);
        return true;
    }

    /// <summary>
    /// Moves the level with this <see cref="HotkeyLevel.Id"/> up or down by <paramref name="percent"/> (for a mouse wheel gesture set to
    /// change it by that much), through the registered action that moves it that way. False when no such action is registered.
    /// </summary>
    public bool RunLevel(string levelId, bool up, int percent)
    {
        var action = Actions.FirstOrDefault(a => a.Level?.Id == levelId && a.LevelDirection > 0 == up);
        if (action is null) return false;
        Dispatcher.UIThread.Post(() => action.RunBy(percent), DispatcherPriority.Send);
        return true;
    }

    // Ahead of everything else queued (input, rendering, layout), so a pop-up or a volume step answers the key or wheel at once
    // even while a page is busy redrawing.
    private static void RunSoon(HotkeyAction action) =>
        Dispatcher.UIThread.Post(action.Run, DispatcherPriority.Send);

    private void OnChanged(HotkeyAction changed)
    {
        // A key combination can only belong to one action.
        if (changed.Gesture.Length > 0)
            foreach (var other in Actions.Where(a => a != changed && a.Gesture == changed.Gesture).ToList())
                other.Gesture = string.Empty;
        foreach (var a in Actions) _stored[a.Id] = a.Export();
        Save();
        Rebind();
    }

    private void Rebind() =>
        _service.Apply(Actions.Where(a => a.Gesture.Length > 0)
            .Select(a => (a.Gesture, (Action)(() => RunSoon(a)))));

    private static Dictionary<string, StoredHotkey> Load() =>
        AppDataStore.Load<Dictionary<string, StoredHotkey>>(FileName) ?? new();

    private void Save() => AppDataStore.Save(FileName, _stored);

    public void Dispose() => _service.Dispose();
}
