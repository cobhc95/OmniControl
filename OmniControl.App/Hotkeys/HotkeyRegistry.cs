using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;

namespace OmniControl.App.Hotkeys;

/// <summary>
/// Every bindable hotkey in the app. A feature calls <see cref="Register"/> once per action;
/// it then shows up on the Shortcuts page (and in search) and is saved and registered automatically.
/// </summary>
public sealed class HotkeyRegistry : IDisposable
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniControl", "hotkeys.json");

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
        for (var i = 0; i < Actions.Count; i++)
            Actions[i].ShowGroupHeader = i == 0 || Actions[i - 1].Category != Actions[i].Category;
        Rebind();
    }

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
            .Select(a => (a.Gesture, (Action)(() => Dispatcher.UIThread.Post(a.Run)))));

    private static Dictionary<string, StoredHotkey> Load()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, StoredHotkey>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_stored));
        }
        catch { /* settings are best effort */ }
    }

    public void Dispose() => _service.Dispose();
}
