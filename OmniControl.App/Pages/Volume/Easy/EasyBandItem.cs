using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>One of the five friendly sliders (Bass ... Treble): its name and level in dB. The user's moves go out through the callback; <see cref="Sync"/> takes the stored value in without sending it back.</summary>
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

    /// <summary>The slider's reach, both ways (<see cref="EasyEq.MaxGainDb"/>).</summary>
    public int Reach => EasyEq.MaxGainDb;

    /// <summary>The slider's lowest value.</summary>
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
