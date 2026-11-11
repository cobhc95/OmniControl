using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages.Volume;

/// <summary>One row of the Volume list: the master or a program (open now, or remembered from before).</summary>
public sealed partial class MixerItem : ObservableObject, IEditorItem
{
    private bool _syncing;

    public MixerItem(string key, bool isMaster) { Key = key; IsMaster = isMaster; }

    string IEditorItem.Label => Name;

    public string Key { get; }
    public bool IsMaster { get; }

    /// <summary>The live level shown in the row's meter (fed by the Volume page while the row is visible).</summary>
    public OmniControl.App.Audio.MeterModel Meter { get; } = new();

    public Action<int>? VolumeChanged { get; init; }
    public Action<int>? PanChanged { get; init; }
    public Action<bool>? MuteChanged { get; init; }
    public Action? FxOpenRequested { get; init; }
    public Action<bool>? FxToggled { get; init; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Initial))] private string _name = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasIcon), nameof(NoIcon))] private Bitmap? _icon;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RowOpacity))] private bool _isOpen = true;
    [ObservableProperty] private int _volume;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PanText))] private int _pan;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private bool _fxOn;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FxText))] private int _fxCount;

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public bool HasIcon => Icon is not null;
    public bool NoIcon => Icon is null;
    public double RowOpacity => IsMaster || IsOpen ? 1 : 0.7;
    public string PanText => Pan == 0 ? "C" : Pan < 0 ? $"L{-Pan}" : $"R{Pan}";
    public string FxText => FxCount > 0 ? $"FX {FxCount}" : "FX";

    partial void OnVolumeChanged(int value) { if (!_syncing) VolumeChanged?.Invoke(value); }
    partial void OnPanChanged(int value) { if (!_syncing) PanChanged?.Invoke(value); }
    partial void OnMutedChanged(bool value) { if (!_syncing) MuteChanged?.Invoke(value); }
    partial void OnFxOnChanged(bool value) { if (!_syncing) FxToggled?.Invoke(value); }

    [RelayCommand] private void ToggleMute() => Muted = !Muted;
    [RelayCommand] private void OpenFx() => FxOpenRequested?.Invoke();
    [RelayCommand] private void ToggleFx() => FxOn = !FxOn;

    /// <summary>Updates the row from the system without sending the values back.</summary>
    public void Sync(string detail, bool isOpen, int volume, int pan, bool muted, bool fxOn, int fxCount)
    {
        _syncing = true;
        Detail = detail;
        IsOpen = isOpen;
        Volume = volume;
        Pan = pan;
        Muted = muted;
        FxOn = fxOn;
        FxCount = fxCount;
        _syncing = false;
    }
}

/// <summary>The last row of the program list: pick a program that isn't playing yet.</summary>
public sealed class AddProgramItem
{
    public required Action<string> Add { get; init; }
}
