using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// An Outputs row: the master or a program (open now, or remembered from before). Adds pan to the shared row.
public sealed partial class MixerItem : VolumeRow
{
    public MixerItem(string key, bool isMaster) : base(key) => IsMaster = isMaster;

    public bool IsMaster { get; }

    public override string? ChainKey => IsMaster ? null : Key;
    public override bool HasPan => true;

    /// With several outputs the master's slider moves Windows' default device only (the others keep their own level).
    [ObservableProperty] private string _volumeTipText = "Volume";
    public override string VolumeTip => VolumeTipText;

    /// Whether the program has sound streams open now (the master always counts as open).
    [ObservableProperty] private bool _isOpen = true;

    partial void OnIsOpenChanged(bool value)
    {
        IsQuiet = !IsMaster && !value;
        CanForget = !IsMaster && !value;
    }

    partial void OnVolumeTipTextChanged(string value)
    {
        OnPropertyChanged(nameof(VolumeTip));
        OnPropertyChanged(nameof(VolumeValueTip));
    }

    /// Updates the row from the system without sending the values back.
    public void Sync(bool isOpen, int volume, int pan, bool muted, bool fxOn, int fxCount)
    {
        IsOpen = isOpen;
        SyncPan(pan);
        Sync(volume, muted, fxOn, fxCount);
    }
}
