using OmniControl.App.Icons;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// A Microphones row: one recording device (its Windows level and mute; its effects run through its own cable).
/// </summary>
public sealed partial class InputItem : VolumeRow
{
    public InputItem(string id) : base(MixerService.InputPrefix + id) => Id = id;

    /// <summary>The device's endpoint id.</summary>
    public string Id { get; }

    public override string? Icon => IconLibrary.Mic;
    public override string VolumeTip => "Input level";
    public override bool HasListen => true;

    /// <summary>The Listen button: turns Windows' "Listen to this device" on or off (the page sets it and reads it back).</summary>
    public Action<bool>? ListenToggled { get; init; }

    /// <summary>The rows of the Listen button's right-click menu (which output it plays on).</summary>
    public Func<IReadOnlyList<Controls.MenuRowItem>>? ListenMenu { get; init; }

    private bool _listening;
    private string _listenOutput = string.Empty;

    /// <summary>Whether Windows plays this input now, as Windows reports it.</summary>
    public bool Listening { get => _listening; private set { if (SetProperty(ref _listening, value)) OnPropertyChanged(nameof(ListenTip)); } }

    public string ListenTip => Listening ? $"Listening on {_listenOutput}" : $"Listen to this mic on {_listenOutput}";

    /// <summary>The page's scan: what Windows says now (<paramref name="output"/> = the output's name).</summary>
    public void SyncListen(bool on, string output)
    {
        _listenOutput = output;
        Listening = on;
        OnPropertyChanged(nameof(ListenTip));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleListen() => ListenToggled?.Invoke(!Listening);
}

/// <summary>
/// A combined device's row: combined speakers in Outputs (plays on its members; no level of its own, no effects), a
/// combined microphone in Microphones (records from its members, with its own level, mute and effects).
/// </summary>
public sealed class ComboItem : VolumeRow
{
    /// <summary>The one word for a combined device in a tag (the Wiring Source column, the menus' entries).</summary>
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

    /// <summary>A combined device sits among the real ones in its Wiring table, so it says what it is.</summary>
    public override string? SourceTag => CombinedTag;
}
