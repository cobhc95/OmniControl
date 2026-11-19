using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace OmniControl.App.Shared.Ui.Controls;

/// The one preset picker (its <see cref="ChoiceItem"/> is the data context): a chip "Preset: name" that opens a menu of the group's presets
/// ending with "Manage presets…". Picking a preset applies it through the ChoiceItem; Manage leaves the selection alone and runs
/// <see cref="ChoiceItem.Manage"/>, which opens the manager at its group. The wheel and Left/Right step presets.
public partial class PresetPicker : UserControl
{
    private ChoiceItem? _choice;
    private Flyout? _flyout;

    public PresetPicker()
    {
        InitializeComponent();
        Menu.Picked += OnPicked;
        Menu.ManageRequested += OnManage;
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_choice is not null) { _choice.PropertyChanged -= OnChoiceChanged; _choice.Availability.PropertyChanged -= OnChoiceChanged; }
        _choice = DataContext as ChoiceItem;
        if (_choice is not null) { _choice.PropertyChanged += OnChoiceChanged; _choice.Availability.PropertyChanged += OnChoiceChanged; }
        Show();
    }

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e) => Show();

    private void Show()
    {
        Chip.Text = _choice is null ? null : $"Preset: {_choice.Selected}";
        Chip.ChipToolTip = _choice?.Availability.Reason;
        Chip.IsEnabled = _choice?.Availability.IsAvailable ?? false;
    }

    private void OnOpened(object? sender, Flyout flyout)
    {
        if (_choice is null) return;
        _flyout = flyout;
        Menu.Show(_choice.Options, _choice.Selected);
    }

    private void OnClosed(object? sender, EventArgs e) => _flyout = null;

    private void OnPicked(object? sender, string name)
    {
        _flyout?.Hide();
        if (_choice is not null && name != _choice.Selected) _choice.Selected = name;
    }

    private void OnManage(object? sender, EventArgs e)
    {
        _flyout?.Hide();
        _choice?.Manage?.Invoke();
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_flyout is not null) return;
        Step(e.Delta.Y > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_flyout is not null || e.Source != Chip.ChipButton || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Left or Key.Right)) return;
        Step(e.Key == Key.Right ? 1 : -1);
        e.Handled = true;
    }

    /// The next or previous preset, stopping at the ends (never "Manage presets…").
    private void Step(int by)
    {
        if (_choice is not { Availability.IsAvailable: true, Options.Count: > 0 } choice) return;
        var at = Math.Max(0, choice.Options.ToList().IndexOf(choice.Selected));
        choice.Selected = choice.Options[Math.Clamp(at + by, 0, choice.Options.Count - 1)];
    }
}
