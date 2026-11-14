using Avalonia.Controls;
using Avalonia.Input;

namespace OmniControl.App.Picker;

/// <summary>
/// The one picker window: a card's action, or a rule's trigger, condition or step, with its settings. Wiring only: it hosts
/// <see cref="PickerMenu"/> and closes with the chosen <see cref="PickResult"/>, or null when cancelled (Esc, or the close button).
/// </summary>
public partial class PickerWindow : Window
{
    private PickerModel? _model;

    public PickerWindow()
    {
        InitializeComponent();
        Menu.Picked += pick => Close(pick);
        Menu.CloseRequested += () => Close(null);
        Opened += (_, _) =>
        {
            if (_model is not null) Menu.Show(_model);
        };
        Closed += (_, _) => Menu.Release();

        // Esc when nothing inside the picker has focus (the menu handles it itself otherwise).
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            Close(null);
            e.Handled = true;
        };
    }

    /// <summary>Opens the window over <paramref name="owner"/> and returns the chosen entry and settings, or null when cancelled.</summary>
    public static Task<PickResult?> ChooseAsync(Window owner, string title, PickerModel model) =>
        new PickerWindow { Title = title, _model = model }.ShowDialog<PickResult?>(owner);

    /// <summary>
    /// What a card's pill (<paramref name="startInSettings"/> false) or cogwheel (true) does: opens the window over the card's window;
    /// the choice, and every live change to the current action's settings, go to the card. Focus then returns to
    /// <paramref name="returnFocusTo"/>, or to <paramref name="picker"/> when that is gone (the new action has no settings).
    /// </summary>
    public static Task ChooseForAsync(Control row, bool startInSettings, Control returnFocusTo, Control picker) =>
        row.DataContext is IPickerCard card ? ChooseForAsync(row, card, startInSettings, returnFocusTo, picker) : Task.CompletedTask;

    /// <summary>The same for <paramref name="card"/>, opened over the window <paramref name="at"/> is in (a control of a pop-down's owner, since a pop-down is not a window).</summary>
    public static async Task ChooseForAsync(Control at, IPickerCard card, bool startInSettings, Control returnFocusTo, Control picker)
    {
        if (TopLevel.GetTopLevel(at) is not Window owner) return;
        var model = card.MenuFor(startInSettings);
        model.Live += card.Pick;
        if (await ChooseAsync(owner, $"Choose an action for {card.Label}", model) is { } picked) card.Pick(picked);
        model.Live -= card.Pick;
        (returnFocusTo.IsEffectivelyVisible ? returnFocusTo : picker).Focus(NavigationMethod.Directional);
    }
}
