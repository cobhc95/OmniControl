using OmniControl.App.Features.Icons;
using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// A Microphones row: one recording device (its Windows level and mute; its effects run through its cable or in Equalizer APO on it).
public sealed partial class InputItem : VolumeRow
{
    public InputItem(string id) : base(MixerService.InputPrefix + id) => Id = id;

    /// The device's endpoint id.
    public string Id { get; }

    public override string? Icon => IconLibrary.Mic;
    public override string VolumeTip => "Input level";
    public override bool HasListen => true;

    /// The Listen button: turns Windows' "Listen to this device" on or off (the page sets it and reads it back).
    public Action<bool>? ListenToggled { get; init; }

    /// The rows of the Listen button's right-click menu (which output it plays on).
    public Func<IReadOnlyList<Shared.Ui.Controls.MenuRowItem>>? ListenMenu { get; init; }

    private bool _listening;
    private string _listenOutput = string.Empty;

    /// Whether Windows plays this input now, as Windows reports it.
    public bool Listening { get => _listening; private set { if (SetProperty(ref _listening, value)) OnPropertyChanged(nameof(ListenTip)); } }

    public string ListenTip => Listening ? $"Listening on {_listenOutput}" : $"Listen to this mic on {_listenOutput}";

    /// The page's scan: what Windows says now (<paramref name="output"/> = the output's name).
    public void SyncListen(bool on, string output)
    {
        _listenOutput = output;
        Listening = on;
        OnPropertyChanged(nameof(ListenTip));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleListen() => ListenToggled?.Invoke(!Listening);
}

/// A combined device's row: combined speakers in Outputs (plays on its members; no level of its own, no effects), a
/// combined microphone in Microphones (records from its members, with its own level, mute and effects).
public sealed class ComboItem : VolumeRow
{
    /// The one word for a combined device in a tag (the Wiring Source column, the menus' entries).
    public const string CombinedTag = "Combined";

    public ComboItem(CombinedDevice device) : base(device.Key)
    {
        IsMicrophone = device.Kind == CombinedKind.Microphones;
        CanForget = true;
    }

    public bool IsMicrophone { get; }
    public override string? Icon => IsMicrophone ? IconLibrary.Mic : IconLibrary.Volume;
    public override bool HasVolume => IsMicrophone;
    public override bool HasEffects => IsMicrophone;

    /// A combined device sits among the real ones in its Wiring table, so it says what it is.
    public override string? SourceTag => CombinedTag;
}
