using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>An Outputs row: the master or a program (open now, or remembered from before). Adds pan to the shared row.</summary>
public sealed partial class MixerItem : VolumeRow
{
    public MixerItem(string key, bool isMaster, Func<ModeGate> newGate) : base(key, newGate) => IsMaster = isMaster;

    public bool IsMaster { get; }

    public override string? ChainKey => IsMaster ? null : Key;
    public override bool HasPan => true;
    public override string RouteArrow => PlaysOnArrow;

    /// <summary>With several outputs the master's slider moves Windows' default device only (the others keep their own level).</summary>
    [ObservableProperty] private string _volumeTipText = "Volume";
    public override string VolumeTip => VolumeTipText;

    /// <summary>Whether the program has sound streams open now (the master always counts as open).</summary>
    [ObservableProperty] private bool _isOpen = true;

    partial void OnIsOpenChanged(bool value)
    {
        IsQuiet = !IsMaster && !value;
        CanForget = !IsMaster && !value;
    }

    partial void OnVolumeTipTextChanged(string value) => OnPropertyChanged(nameof(VolumeTip));

    /// <summary>Updates the row from the system without sending the values back.</summary>
    public void Sync(bool isOpen, int volume, int pan, bool muted, bool fxOn, int fxCount)
    {
        IsOpen = isOpen;
        SyncPan(pan);
        Sync(volume, muted, fxOn, fxCount);
    }
}
