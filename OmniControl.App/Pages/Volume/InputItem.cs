using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>One row of the Inputs group: a recording device (microphone, line in...) with its level, mute, FX button and a live meter.</summary>
public sealed partial class InputItem : ObservableObject, IEditorItem
{
    private bool _syncing;

    public InputItem(string id, string fxKey) { Id = id; FxKey = fxKey; }

    string IEditorItem.Label => Name;

    public string Id { get; }

    /// <summary>The key this device's FX chain is stored and opened under.</summary>
    public string FxKey { get; }
    public MeterModel Meter { get; } = new();

    public Action<int>? VolumeChanged { get; init; }
    public Action<bool>? MuteChanged { get; init; }
    public Action? FxOpenRequested { get; init; }
    public Action<bool>? FxToggled { get; init; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Initial))] private string _name = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private int _volume;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private bool _fxOn;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FxText))] private int _fxCount;

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string FxText => FxCount > 0 ? $"FX {FxCount}" : "FX";

    partial void OnVolumeChanged(int value) { if (!_syncing) VolumeChanged?.Invoke(value); }
    partial void OnMutedChanged(bool value) { if (!_syncing) MuteChanged?.Invoke(value); }
    partial void OnFxOnChanged(bool value) { if (!_syncing) FxToggled?.Invoke(value); }

    [RelayCommand] private void ToggleMute() => Muted = !Muted;
    [RelayCommand] private void OpenFx() => FxOpenRequested?.Invoke();
    [RelayCommand] private void ToggleFx() => FxOn = !FxOn;

    /// <summary>Updates the row from the system without sending the values back.</summary>
    public void Sync(string detail, int volume, bool muted)
    {
        _syncing = true;
        Detail = detail;
        Volume = volume;
        Muted = muted;
        _syncing = false;
    }

    public void SyncFx(bool on, int count)
    {
        _syncing = true;
        FxOn = on;
        FxCount = count;
        _syncing = false;
    }
}
