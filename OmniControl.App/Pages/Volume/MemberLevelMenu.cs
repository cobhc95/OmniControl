using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The small pop-down of a combined microphone's member chip: the member's name, its level in the mix (0-200 %, double-click resets to
/// 100) and its mute. Changes apply at once (principle 2); the page reads the stored values each time it opens.
/// </summary>
public sealed partial class MemberLevelMenu : ObservableObject
{
    private readonly Func<(int Level, bool Muted)> _read;
    private readonly Action<int, bool> _write;
    private bool _syncing;

    /// <param name="read">The stored level and mute.</param>
    /// <param name="write">Stores a new level and mute (the engine applies them to the running mix).</param>
    public MemberLevelMenu(string name, Func<(int Level, bool Muted)> read, Action<int, bool> write)
    {
        Name = name;
        _read = read;
        _write = write;
        Refresh();
    }

    public string Name { get; }

    public int Minimum => Percent.Min;
    public int Maximum => Percent.BoostMax;
    public double ResetValue => CombinedDevice.DefaultMemberLevel;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LevelText))] private int _level;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MuteText))] private bool _muted;

    public string LevelText => $"{Level} %";
    public string MuteText => Muted ? "Muted" : "Mute";

    /// <summary>Reads the stored values again (the pop-down is opening).</summary>
    public void Refresh()
    {
        var (level, muted) = _read();
        _syncing = true;
        Level = level;
        Muted = muted;
        _syncing = false;
    }

    [RelayCommand] private void ToggleMute() => Muted = !Muted;

    partial void OnLevelChanged(int value)
    {
        if (!_syncing) _write(value, Muted);
    }

    partial void OnMutedChanged(bool value)
    {
        if (!_syncing) _write(Level, value);
    }
}
