using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages.Easy;

/// One of the five friendly sliders (Bass ... Treble): its name and level in dB. The user's moves go out through the callback; <see cref="Sync"/> takes the stored value in without sending it back.
public sealed partial class EasyBandItem : ObservableObject
{
    private readonly Action<EasyBandItem> _changed;
    private bool _syncing;

    public EasyBandItem(int index, string name, Action<EasyBandItem> changed)
    {
        Index = index;
        Name = name;
        _changed = changed;
    }

    public int Index { get; }
    public string Name { get; }

    /// The slider's reach, both ways (<see cref="EasyEq.MaxGainDb"/>).
    public int Reach => EasyEq.MaxGainDb;

    /// The slider's lowest value.
    public int Floor => -EasyEq.MaxGainDb;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Text))] private int _value;

    public string Text => Value > 0 ? $"+{Value} dB" : $"{Value} dB";

    partial void OnValueChanged(int value)
    {
        if (!_syncing) _changed(this);
    }

    public void Sync(int value)
    {
        _syncing = true;
        Value = value;
        _syncing = false;
    }
}
