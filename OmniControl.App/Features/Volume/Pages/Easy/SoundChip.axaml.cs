using System.ComponentModel;

namespace OmniControl.App.Features.Volume.Pages.Easy;

/// Wiring only: the chip shows the row's sound name; its menu lists the sound chips (a pick applies it through the chip's own command) and
/// ends with the row that shows or hides the drawer of sliders.
public partial class SoundChip : UserControl
{
    private EasySound? _sound;
    private Flyout? _flyout;

    public SoundChip()
    {
        InitializeComponent();
        Menu.Picked += OnPicked;
        Menu.ManageRequested += OnSliders;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_sound is not null) _sound.PropertyChanged -= OnSoundChanged;
        _sound = (DataContext as EasyRow)?.Sound;
        if (_sound is not null) _sound.PropertyChanged += OnSoundChanged;
        Show();
    }

    private void OnSoundChanged(object? sender, PropertyChangedEventArgs e) => Show();

    private void Show() => Chip.Text = _sound is null ? null : $"Sound: {_sound.Name}";

    private void OnOpened(object? sender, Flyout flyout)
    {
        if (_sound is null) return;
        _flyout = flyout;
        Menu.FooterText = _sound.IsOpen ? "Hide the sliders" : "Show the sliders";
        Menu.Show(_sound.Chips.Select(c => c.Name), _sound.Chips.FirstOrDefault(c => c.IsSelected)?.Name ?? string.Empty);
    }

    private void OnClosed(object? sender, EventArgs e) => _flyout = null;

    private void OnPicked(object? sender, string name)
    {
        _flyout?.Hide();
        if (_sound?.Chips.FirstOrDefault(c => c.Name == name) is { } chip && chip.PickCommand.CanExecute(null)) chip.PickCommand.Execute(null);
    }

    private void OnSliders(object? sender, EventArgs e)
    {
        _flyout?.Hide();
        _sound?.ToggleCommand.Execute(null);
    }
}
