using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Models;
using OmniControl.App.Services;

namespace OmniControl.App.ViewModels;

public sealed class HotkeyRow
{
    public HotkeyRow(HotkeyBinding b, bool conflict)
    {
        Binding = b; IsConflict = conflict;
        KeyText = VolumeViewModel.Describe(b.Modifiers, b.Key);
        ScopeText = conflict ? "Conflicts" : b.Scope == HotkeyScope.Global ? "Global" : "Active app";
    }
    public HotkeyBinding Binding { get; }
    public string Action => Binding.Action;
    public string KeyText { get; }
    public string ScopeText { get; }
    public bool IsConflict { get; }
}

public partial class VolumeViewModel : ViewModelBase, IDisposable
{
    private readonly MainViewModel _main;
    private readonly GlobalHotkeyService _hotkeys;
    private List<HotkeyBinding> _bindings;
    private HotkeyBinding? _editing;
    private HotkeyMods _pendingMods;
    private int _pendingKey;

    public ObservableCollection<HotkeyRow> Rows { get; } = new();

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _recordingFor = "";
    [ObservableProperty] private string _recorderText = "Press the keys you want to use…";
    [ObservableProperty] private string _conflictMessage = "";
    [ObservableProperty] private string _tableConflictNote = "";
    [ObservableProperty] private int _masterVolume = 30;
    [ObservableProperty] private int _spotifyVolume = 60;
    [ObservableProperty] private int _chromeVolume = 45;
    [ObservableProperty] private string _muteLabel = "Mute all";
    [ObservableProperty] private string _noteText = "Press Change to record a new key, then Save. Tab to every control; Enter or Space activates.";
    private int _prevMaster = 30;
    private bool _suppressSystemSync;

    public bool HasConflictMessage => !string.IsNullOrEmpty(ConflictMessage);
    public bool HasTableConflict => !string.IsNullOrEmpty(TableConflictNote);
    partial void OnConflictMessageChanged(string value) => OnPropertyChanged(nameof(HasConflictMessage));
    partial void OnTableConflictNoteChanged(string value) => OnPropertyChanged(nameof(HasTableConflict));

    public VolumeViewModel(MainViewModel main)
    {
        _main = main;
        _bindings = HotkeyStore.Load();
        _hotkeys = new GlobalHotkeyService(b => _main.RunExternal(b.Command));
        _suppressSystemSync = true;
        try
        {
            if (_main.Engine.System.GetVolume(out var v).Success) MasterVolume = v;
        }
        catch { }
        _suppressSystemSync = false;
        Rebuild();
        Register();
    }

    partial void OnMasterVolumeChanged(int value)
    {
        if (_suppressSystemSync) return;
        try { _main.Engine.System.SetVolume(value); } catch { }
    }

    [RelayCommand]
    private void Mute()
    {
        _prevMaster = MasterVolume;
        _suppressSystemSync = true; MasterVolume = 0; _suppressSystemSync = false;
        try { _main.Engine.System.SetVolume(0); } catch { }
        MuteLabel = "Muted"; NoteText = "Muted";
    }

    [RelayCommand]
    private void Unmute()
    {
        _suppressSystemSync = true; MasterVolume = _prevMaster; _suppressSystemSync = false;
        try { _main.Engine.System.SetVolume(_prevMaster); } catch { }
        MuteLabel = "Mute all"; NoteText = "Unmuted";
    }

    private void Register() => _hotkeys.Apply(_bindings, _main.Log);

    private bool Conflicts(HotkeyBinding b, IEnumerable<HotkeyBinding> all) =>
        b.Enabled && all.Any(o => o.Id != b.Id && o.Enabled && o.SameKeys(b));

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var b in _bindings) Rows.Add(new HotkeyRow(b, Conflicts(b, _bindings)));
        var c = Rows.FirstOrDefault(r => r.IsConflict);
        if (c != null)
        {
            var other = _bindings.First(o => o.Id != c.Binding.Id && o.Enabled && o.SameKeys(c.Binding));
            TableConflictNote = $"{c.KeyText} is used by both {other.Action} and {c.Action}.";
        }
        else TableConflictNote = "";
    }

    [RelayCommand]
    private void Change(HotkeyRow? row)
    {
        if (row == null) return;
        _editing = row.Binding;
        _pendingKey = 0; _pendingMods = HotkeyMods.None;
        RecordingFor = row.Action;
        RecorderText = "Press the keys you want to use…";
        ConflictMessage = "";
        IsRecording = true;
    }

    [RelayCommand]
    private void Cancel() { IsRecording = false; _editing = null; ConflictMessage = ""; }

    [RelayCommand]
    private void Save()
    {
        if (_editing == null || _pendingKey == 0) return;
        if (CheckConflict()) return;
        var updated = _editing with { Modifiers = _pendingMods, Key = _pendingKey };
        _bindings = _bindings.Select(b => b.Id == updated.Id ? updated : b).ToList();
        HotkeyStore.Save(_bindings);
        Rebuild();
        Register();
        IsRecording = false; _editing = null;
    }

    private bool CheckConflict()
    {
        var other = _bindings.FirstOrDefault(o => o.Id != _editing!.Id && o.Enabled && o.Modifiers == _pendingMods && o.Key == _pendingKey);
        ConflictMessage = other == null ? "" : $"{RecorderText} is already used by {other.Action}. Pick another key.";
        return other != null;
    }

    /// <summary>Called from the recorder's KeyDown. Returns true when the key was consumed.</summary>
    public bool OnRecorderKey(Key key, KeyModifiers mods)
    {
        if (!IsRecording) return false;
        if (key == Key.Escape) { Cancel(); return true; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None) return true;
        var m = HotkeyMods.None;
        if (mods.HasFlag(KeyModifiers.Control)) m |= HotkeyMods.Ctrl;
        if (mods.HasFlag(KeyModifiers.Alt)) m |= HotkeyMods.Alt;
        if (mods.HasFlag(KeyModifiers.Shift)) m |= HotkeyMods.Shift;
        if (mods.HasFlag(KeyModifiers.Meta)) m |= HotkeyMods.Win;
        if (m == HotkeyMods.None) { RecorderText = "Hold Ctrl, Alt, Shift or Win with a key…"; return true; }
        var vk = Avalonia.Win32.Input.KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return true;
        _pendingMods = m; _pendingKey = vk;
        RecorderText = Describe(m, vk);
        CheckConflict();
        return true;
    }

    public static string Describe(HotkeyMods m, int vk)
    {
        var parts = new List<string>();
        if (m.HasFlag(HotkeyMods.Ctrl)) parts.Add("Ctrl");
        if (m.HasFlag(HotkeyMods.Alt)) parts.Add("Alt");
        if (m.HasFlag(HotkeyMods.Shift)) parts.Add("Shift");
        if (m.HasFlag(HotkeyMods.Win)) parts.Add("Win");
        string name;
        try
        {
            var k = Avalonia.Win32.Input.KeyInterop.KeyFromVirtualKey(vk, 0);
            name = k.ToString();
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name[1..];
            if (k == Key.None) name = $"VK {vk:X2}";
        }
        catch { name = $"VK {vk:X2}"; }
        parts.Add(name);
        return string.Join(" + ", parts);
    }

    public void Dispose() => _hotkeys.Dispose();
}
