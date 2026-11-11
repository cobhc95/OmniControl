using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Models;
using OmniControl.App.Services;

namespace OmniControl.App.ViewModels;

public abstract class PlaceholderViewModel : ViewModelBase
{
    public abstract string Heading { get; }
    public abstract string Description { get; }
    public virtual bool IsExperimental => false;
}

public sealed class MouseProfile
{
    public required string Name { get; init; }
    public required string Subtitle { get; init; }
    public override string ToString() => Name;
}

public sealed partial class MouseButtonRow : ViewModelBase
{
    [ObservableProperty] private string _actionText;
    [ObservableProperty] private bool _isUnchanged;
    public string ButtonName { get; }
    public MouseButtonRow(string name, string action, bool unchanged = false)
    {
        ButtonName = name; _actionText = action; _isUnchanged = unchanged;
    }
}

public partial class MouseViewModel : ViewModelBase
{
    private readonly MainViewModel? _main;
    public ObservableCollection<MouseProfile> Profiles { get; } = new();
    public ObservableCollection<MouseButtonRow> ButtonRows { get; } = new();
    [ObservableProperty] private MouseProfile? _selectedProfile;
    [ObservableProperty] private int _sensitivity = 7;
    [ObservableProperty] private string _statusText = "Remapping needs a low-level hook running in the background. Changes apply to the active profile only.";
    private static readonly string[] Actions =
    [
        "Default system action", "Browser back", "Ctrl + click (open link in new tab)",
        "Paste", "Copy", "Middle click", "Not set"
    ];

    public MouseViewModel() { Init(); }
    public MouseViewModel(MainViewModel main) { _main = main; Init(); }
    private void Init()
    {
        Profiles.Add(new() { Name = "Default", Subtitle = "Applies everywhere" });
        Profiles.Add(new() { Name = "Chrome", Subtitle = "chrome.exe" });
        Profiles.Add(new() { Name = "Blender", Subtitle = "blender.exe" });
        SelectedProfile = Profiles[0];
        ButtonRows.Add(new("Left click", "Default system action", true));
        ButtonRows.Add(new("Right click", "Default system action", true));
        ButtonRows.Add(new("Middle click", "Ctrl + click (open link in new tab)"));
        ButtonRows.Add(new("Mouse 4 (X1)", "Browser back"));
        ButtonRows.Add(new("Mouse 5 (X2)", "Not set"));
        ButtonRows.Add(new("Wheel tilt left", "Browser back"));
    }
    partial void OnSelectedProfileChanged(MouseProfile? value)
    {
        if (value != null) StatusText = $"Active profile: {value.Name}";
    }
    [RelayCommand]
    private void AddProfile()
    {
        var n = Profiles.Count + 1;
        var p = new MouseProfile { Name = $"Profile {n}", Subtitle = "new app" };
        Profiles.Add(p); SelectedProfile = p;
    }
    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null || Profiles.Count <= 1) return;
        var idx = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[Math.Clamp(idx, 0, Profiles.Count - 1)];
    }
    [RelayCommand]
    private void Apply() => StatusText = $"Hook updated for {SelectedProfile?.Name ?? "Default"}";
    [RelayCommand]
    private void ChangeRow(MouseButtonRow? row)
    {
        if (row == null) return;
        var i = Array.IndexOf(Actions, row.ActionText);
        row.ActionText = Actions[(i + 1) % Actions.Length];
        row.IsUnchanged = false;
        StatusText = $"{row.ButtonName} → {row.ActionText}";
    }
}

/// <summary>One rebindable keyboard/media key: physical key label + what it does.</summary>
public sealed partial class KeyRow : ViewModelBase
{
    [ObservableProperty] private string _keyText;
    [ObservableProperty] private string _actionText;
    [ObservableProperty] private bool _isUnchanged;
    public string KeyName { get; }
    public KeyRow(string name, string key, string action, bool unchanged = false)
    {
        KeyName = name; _keyText = key; _actionText = action; _isUnchanged = unchanged;
    }
}

/// <summary>
/// Keyboard tab: key rebinding (which key does what) for media + keyboard keys —
/// not global hotkeys (those live on the Hotkeys tab). Change cycles the action,
/// the recorder bar captures the new key from a real keydown.
/// </summary>
public partial class KeyboardViewModel : ViewModelBase
{
    private readonly MainViewModel? _main;
    public ObservableCollection<KeyRow> KeyRows { get; } = new();
    [ObservableProperty] private string _statusText = "Rebind keys: Change cycles an action, or press Change then press the new key.";
    [ObservableProperty] private string _recordingFor = "";
    [ObservableProperty] private string _recorderText = "Press the new key…";
    [ObservableProperty] private bool _isRecording;
    private KeyRow? _editing;
    private static readonly string[] Actions =
    [
        "Default system action", "Brightness up", "Brightness down", "Monitor volume up",
        "Volume up", "Volume down", "Mute", "Keyboard: F9 (unused)", "Copy", "Paste",
        "Ctrl + click (open link in new tab)", "Not set"
    ];

    public KeyboardViewModel() { Init(); }
    public KeyboardViewModel(MainViewModel main) { _main = main; Init(); }
    private void Init()
    {
        KeyRows.Add(new("Media: Next", "MediaNext", "Volume up"));
        KeyRows.Add(new("Media: Prev", "MediaPrev", "Volume down"));
        KeyRows.Add(new("Media: Play/Pause", "MediaPlayPause", "Default system action"));
        KeyRows.Add(new("Media: Mute", "MediaMute", "Mute"));
        KeyRows.Add(new("Fn Lock", "FnLock", "Default system action"));
        KeyRows.Add(new("Caps Lock", "CapsLock", "Default system action"));
        KeyRows.Add(new("F13", "F13", "Keyboard: F9 (unused)"));
        KeyRows.Add(new("F14", "F14", "Not set"));
    }
    [RelayCommand]
    private void ChangeRow(KeyRow? row)
    {
        if (row == null) return;
        var i = Array.IndexOf(Actions, row.ActionText);
        row.ActionText = Actions[(i + 1) % Actions.Length];
        row.IsUnchanged = false;
        StatusText = $"{row.KeyName} → {row.ActionText}";
    }
    [RelayCommand]
    private void StartRec(KeyRow? row)
    {
        if (row == null) return;
        _editing = row;
        RecordingFor = row.KeyName;
        RecorderText = "Press the new key…";
        IsRecording = true;
    }
    [RelayCommand] private void Cancel() { IsRecording = false; _editing = null; }
    [RelayCommand]
    private void Save()
    {
        if (_editing != null && RecorderText.Length > 0 && RecorderText != "Press the new key…")
        {
            _editing.KeyText = RecorderText;
            _editing.IsUnchanged = false;
            StatusText = $"{_editing.KeyName} → {_editing.ActionText} ({RecorderText})";
        }
        IsRecording = false; _editing = null;
    }
    [RelayCommand] private void Reset() { KeyRows.Clear(); Init(); IsRecording = false; StatusText = "Rebinds reset to defaults"; }
    public bool OnKeyPressed(Avalonia.Input.Key key)
    {
        if (!IsRecording) return false;
        if (key == Avalonia.Input.Key.Escape) { Cancel(); return true; }
        if (key is Avalonia.Input.Key.LeftCtrl or Avalonia.Input.Key.RightCtrl or Avalonia.Input.Key.LeftAlt
            or Avalonia.Input.Key.RightAlt or Avalonia.Input.Key.LeftShift or Avalonia.Input.Key.RightShift
            or Avalonia.Input.Key.LWin or Avalonia.Input.Key.RWin or Avalonia.Input.Key.System
            or Avalonia.Input.Key.None) return true;
        RecorderText = key.ToString();
        return true;
    }
}

public sealed class HotkeyBindingRow : ViewModelBase
{
    private readonly HotkeyBinding _binding;
    public HotkeyBindingRow(HotkeyBinding b, bool conflict)
    {
        _binding = b; IsConflict = conflict;
        KeyText = VolumeViewModel.Describe(b.Modifiers, b.Key);
        CommandText = b.Command;
        ScopeText = conflict ? $"Conflicts with row 1" : b.Scope == HotkeyScope.Global ? "Global" : "Only in Chrome";
    }
    public HotkeyBinding Binding => _binding;
    public string KeyText { get; }
    public string CommandText { get; }
    public string ScopeText { get; }
    public bool IsOn => _binding.Enabled;
    public bool IsConflict { get; }
}

public partial class HotkeysViewModel : ViewModelBase
{
    private readonly MainViewModel? _main;
    private readonly GlobalHotkeyService? _hotkeys;
    private List<HotkeyBinding> _bindings;
    private HotkeyBinding? _editing;
    private HotkeyMods _pendingMods;
    private int _pendingKey;
    public ObservableCollection<HotkeyBindingRow> Rows { get; } = new();
    [ObservableProperty] private string _recorderText = "Press the keys you want to bind…";
    [ObservableProperty] private string _newCommandText = "PL2779Q b +5";
    [ObservableProperty] private bool _isRecording;
    public HotkeysViewModel() { _bindings = HotkeyStore.Load(); }
    public HotkeysViewModel(MainViewModel main)
    {
        _main = main; _bindings = HotkeyStore.Load();
        _hotkeys = new GlobalHotkeyService(b => _main.RunExternal(b.Command));
        Rebuild(); Register();
        if (_bindings.Count == 0 || _bindings.All(b => b.Command.Contains("SYS v")))
            EnsureMockDefaults();
    }
    private void EnsureMockDefaults()
    {
        _bindings = new List<HotkeyBinding>
        {
            new("hk1", "Brightness up", HotkeyMods.Ctrl | HotkeyMods.Alt, 0x26, HotkeyScope.Global, true, "PL2779Q b +5"),
            new("hk2", "Brightness down", HotkeyMods.Ctrl | HotkeyMods.Alt, 0x28, HotkeyScope.Global, true, "PL2779Q b -5"),
            new("hk3", "Monitors off", HotkeyMods.Win, 0x70, HotkeyScope.Global, true, "ALL p off"),
            new("hk4", "Mute", HotkeyMods.Ctrl | HotkeyMods.Shift, 0x4D, HotkeyScope.ActiveApp, false, "SYS m 1"),
            new("hk5", "LCD brightness", HotkeyMods.Ctrl | HotkeyMods.Alt, 0x26, HotkeyScope.Global, true, "LCD b +10"),
        };
        HotkeyStore.Save(_bindings); Rebuild();
    }
    private void Register() { Action<string> log = _main != null ? _main.Log : _ => { }; _hotkeys?.Apply(_bindings, log); }
    private bool Conflicts(HotkeyBinding b) => b.Enabled && _bindings.Any(o => o.Id != b.Id && o.Enabled && o.SameKeys(b));
    private void Rebuild()
    {
        Rows.Clear();
        foreach (var b in _bindings) Rows.Add(new HotkeyBindingRow(b, Conflicts(b)));
    }
    [RelayCommand] private void New() { _editing = null; RecorderText = "Press the keys you want to bind…"; IsRecording = true; }
    [RelayCommand] private void Toggle(HotkeyBindingRow? row)
    {
        if (row == null) return;
        _bindings = _bindings.Select(b => b.Id == row.Binding.Id ? b with { Enabled = !b.Enabled } : b).ToList();
        HotkeyStore.Save(_bindings); Rebuild(); Register();
    }
    [RelayCommand] private void Edit(HotkeyBindingRow? row)
    {
        if (row == null) return;
        _editing = row.Binding; NewCommandText = row.Binding.Command;
        RecorderText = VolumeViewModel.Describe(row.Binding.Modifiers, row.Binding.Key);
        _pendingMods = row.Binding.Modifiers; _pendingKey = row.Binding.Key; IsRecording = true;
    }
    [RelayCommand] private void Save()
    {
        if (!IsRecording) return;
        if (_pendingKey == 0) return;
        if (string.IsNullOrWhiteSpace(NewCommandText)) return;
        if (_editing != null)
            _bindings = _bindings.Select(b => b.Id == _editing.Id ? b with { Modifiers = _pendingMods, Key = _pendingKey, Command = NewCommandText } : b).ToList();
        else
            _bindings.Add(new HotkeyBinding(Guid.NewGuid().ToString("N")[..8], "Custom", _pendingMods, _pendingKey, HotkeyScope.Global, true, NewCommandText));
        HotkeyStore.Save(_bindings); Rebuild(); Register(); IsRecording = false; _editing = null;
    }
    [RelayCommand] private void Cancel() { IsRecording = false; _editing = null; }
    public bool OnRecorderKey(Key key, KeyModifiers mods)
    {
        if (key == Key.Escape) { Cancel(); return true; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None) return true;
        var m = HotkeyMods.None;
        if (mods.HasFlag(KeyModifiers.Control)) m |= HotkeyMods.Ctrl;
        if (mods.HasFlag(KeyModifiers.Alt)) m |= HotkeyMods.Alt;
        if (mods.HasFlag(KeyModifiers.Shift)) m |= HotkeyMods.Shift;
        if (mods.HasFlag(KeyModifiers.Meta)) m |= HotkeyMods.Win;
        var vk = Avalonia.Win32.Input.KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return true;
        _pendingMods = m; _pendingKey = vk;
        RecorderText = VolumeViewModel.Describe(m, vk);
        return true;
    }
}

public sealed class SceneRow
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public override string ToString() => Name;
}

public partial class SmartHomeViewModel : ViewModelBase
{
    private readonly MainViewModel? _main;
    public ObservableCollection<SceneRow> Scenes { get; } = new();
    public ObservableCollection<string> DiscoveryLog { get; } = new();
    [ObservableProperty] private SceneRow? _selectedScene;
    [ObservableProperty] private bool _bulbOn = true;
    [ObservableProperty] private int _bulbBrightness = 60;
    [ObservableProperty] private bool _deskOn;
    [ObservableProperty] private bool _lightBarOn = true;
    [ObservableProperty] private int _selectedColor;
    [ObservableProperty] private bool _bedroomOffline = true;
    [ObservableProperty] private string _statusText = "3 devices found";
    private bool _suppressScene;
    public SmartHomeViewModel() { Init(); }
    public SmartHomeViewModel(MainViewModel main) { _main = main; Init(); }
    private void Init()
    {
        Scenes.Add(new() { Name = "Movie night", Description = "Dim lights, desk lamp off, monitor brightness 15" });
        Scenes.Add(new() { Name = "Morning", Description = "Lights to 80%, monitor brightness 50" });
        Scenes.Add(new() { Name = "Leaving", Description = "All devices off, monitors powered off" });
        DiscoveryLog.Add("[18:02:11] Scan 192.168.1.0/24 · 3 devices found");
        DiscoveryLog.Add("[18:02:14] Handshake OK · Living room bulb");
        DiscoveryLog.Add("[18:02:14] Handshake OK · Desk lamp");
        DiscoveryLog.Add("[18:02:19] Timeout · Bedroom plug");
        _suppressScene = true;
        SelectedScene = Scenes[0];
        _suppressScene = false;
    }
    partial void OnSelectedSceneChanged(SceneRow? value)
    {
        if (value == null || _suppressScene) return;
        switch (value.Name)
        {
            case "Movie night": BulbBrightness = 15; BulbOn = true; DeskOn = false; break;
            case "Morning": BulbBrightness = 80; BulbOn = true; DeskOn = true; break;
            case "Leaving": BulbOn = false; DeskOn = false; LightBarOn = false; break;
        }
        StatusText = $"Scene: {value.Name}";
    }
    [RelayCommand]
    private void SelectScene(SceneRow? s)
    {
        if (s == null) return;
        if (SelectedScene != s) SelectedScene = s; else OnSelectedSceneChanged(s);
        var cmd = s.Name switch { "Movie night" => "ALL b 15", "Morning" => "ALL b 50", "Leaving" => "ALL p off", _ => "" };
        if (cmd.Length > 0) _main?.RunExternal(cmd);
    }
    [RelayCommand] private void AddDevice() { DiscoveryLog.Add($"[{DateTime.Now:HH:mm:ss}] Manual add requested"); }
    [RelayCommand] private void Retry() { BedroomOffline = false; DiscoveryLog.Add($"[{DateTime.Now:HH:mm:ss}] Handshake OK · Bedroom plug"); StatusText = "Bedroom plug online"; }
    [RelayCommand] private void SelectColor(object? p) { if (int.TryParse(p?.ToString(), out var idx)) { SelectedColor = idx; StatusText = $"Colour {idx + 1}"; } }
    [RelayCommand] private void ToggleBulb() { BulbOn = !BulbOn; }
    [RelayCommand] private void ToggleDesk() { DeskOn = !DeskOn; }
    [RelayCommand] private void ToggleLightBar() { LightBarOn = !LightBarOn; }
}
