namespace OmniControl.App.Shared.Ui.Pages;

/// One state button (Button.statetoggle): an on/off state shown as its words and icon for the value
/// it has now ("Sound on" / "Sound muted"); a click turns it over. On is the muted look (red). Drawn on a card's extras line
/// (<see cref="SliderItem.Extras"/>).
public sealed partial class StateButtonItem : ObservableObject
{
    private readonly string _onWords, _offWords, _onIcon, _offIcon;
    private readonly Action<bool> _apply;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Words), nameof(Icon))] private bool _isOn;

    /// What the state acts on ("Default speaker: Speakers"), as the button's tooltip; empty: none. May change while shown.
    [ObservableProperty] private string _tip = string.Empty;

    public StateButtonItem(bool isOn, string onWords, string offWords, string onIcon, string offIcon, Action<bool> apply)
    {
        _isOn = isOn;
        (_onWords, _offWords, _onIcon, _offIcon, _apply) = (onWords, offWords, onIcon, offIcon, apply);
    }

    public string Words => IsOn ? _onWords : _offWords;

    public string Icon => IsOn ? _onIcon : _offIcon;

    /// Shows a state read from elsewhere without applying it.
    public void Load(bool isOn) => IsOn = isOn;

    [RelayCommand]
    private void Toggle()
    {
        IsOn = !IsOn;
        _apply(IsOn);
    }
}
