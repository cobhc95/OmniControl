using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages;

/// <summary>
/// One state button (Button.statetoggle, docs/mockups/sound-mute-buttons.html): an on/off state shown as its words and icon for the value
/// it has now ("Sound on" / "Sound muted"); a click turns it over. On is the muted look (red). Drawn on a card's extras line
/// (<see cref="SliderItem.Extras"/>).
/// </summary>
public sealed partial class StateButtonItem : ObservableObject
{
    private readonly string _onWords, _offWords, _onIcon, _offIcon;
    private readonly Action<bool> _apply;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Words), nameof(Icon))] private bool _isOn;

    /// <summary>What the state acts on ("Default speaker: Speakers"), as the button's tooltip; empty: none. May change while shown.</summary>
    [ObservableProperty] private string _tip = string.Empty;

    public StateButtonItem(bool isOn, string onWords, string offWords, string onIcon, string offIcon, Action<bool> apply)
    {
        _isOn = isOn;
        (_onWords, _offWords, _onIcon, _offIcon, _apply) = (onWords, offWords, onIcon, offIcon, apply);
    }

    public string Words => IsOn ? _onWords : _offWords;

    public string Icon => IsOn ? _onIcon : _offIcon;

    /// <summary>Shows a state read from elsewhere without applying it.</summary>
    public void Load(bool isOn) => IsOn = isOn;

    [RelayCommand]
    private void Toggle()
    {
        IsOn = !IsOn;
        _apply(IsOn);
    }
}
