using System.ComponentModel;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Volume.Pages.Easy;

/// Wiring only: the chip shows the row's sound name; its menu lists the sound chips (a pick applies it through the chip's own command) and
/// ends with the row that shows or hides the drawer of sliders.
public partial class SoundChip : UserControl
{
    private EasySound? _sound;
    private Flyout? _flyout;

    public SoundChip() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Follow();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Follow();
    }

    // The sound is heard only while the chip is on screen, so a view let go (a released main window) is never held by its model.
    private void Follow()
    {
        var sound = this.IsAttachedToVisualTree() ? (DataContext as EasyRow)?.Sound : null;
        if (sound == _sound) return;
        if (_sound is not null) _sound.PropertyChanged -= OnSoundChanged;
        _sound = sound;
        if (_sound is not null) _sound.PropertyChanged += OnSoundChanged;
        Show();
    }

    private void OnSoundChanged(object? sender, PropertyChangedEventArgs e) => Show();

    private void Show() => Chip.Text = _sound is null ? null : $"Sound: {_sound.Name}";

    private void OnOpened(object? sender, Flyout flyout)
    {
        if (_sound is not { } sound) return;
        _flyout = flyout;
        Menu.ItemsSource = PresetMenu.Rows(sound.Chips.Select(c => c.Name), sound.Chips.FirstOrDefault(c => c.IsSelected)?.Name ?? string.Empty, Pick,
            new MenuRowItem(sound.IsOpen ? "Hide the sliders" : "Show the sliders", string.Empty, false, string.Empty, sound.ToggleCommand));
    }

    private void OnClosed(object? sender, EventArgs e) => _flyout = null;

    private void OnPicked(object? sender, EventArgs e) => _flyout?.Hide();

    private void Pick(string name)
    {
        if (_sound?.Chips.FirstOrDefault(c => c.Name == name) is { } chip && chip.PickCommand.CanExecute(null)) chip.PickCommand.Execute(null);
    }
}
