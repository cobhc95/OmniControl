using System.Diagnostics;
using Avalonia.Input;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// The one picker window: a card's action, or a rule's trigger, condition or step, with its settings. Wiring only: it hosts
/// <see cref="PickerMenu"/> and closes with the chosen <see cref="PickResult"/>, or null when cancelled (Esc, or the close button).
/// Opened by <see cref="Manage"/> it is the Actions and hotkeys window instead: no "Use this ...", and a top bar. In both modes it shows the
/// keys of the entries (<see cref="ManageMode"/>, read from the live master list, so the callers that pick never hand it over) and holds the
/// other keys from Windows while a key may be pressed into it (<see cref="HotkeyRegistry.Hold"/>).
public partial class PickerWindow : Window
{
    private PickerModel? _model;

    // Set once at start-up by the shell (UseKeys): the one live keys part every picker window shows. A set-once static, not changing state,
    // because the windows are opened from many pickers that must not be handed the master list.
    private static ManageMode? _keys;

    /// Gives every picker window the keys part (once, at start-up).
    internal static void UseKeys(ManageMode keys) => _keys ??= keys;

    /// Makes the window's panes once, unseen, after start-up, so the first opening does not wait for their layout code to load.
    internal static void Warm() => _ = new PickerMenu();

    public PickerWindow()
    {
        InitializeComponent();
        Menu.Picked += pick =>
        {
            Recents.Remember(pick.Choice.Spec);
            Close(pick);
        };
        Menu.CloseRequested += () => Close(null);
        Opened += (_, _) =>
        {
            if (_model is not null) Menu.Show(_model);
        };
        Closed += (_, _) => Leave();
    }

    /// The Actions and hotkeys window (non-modal, kept while hidden; <see cref="ActionsWindowService"/> shows it and gives it a model in
    /// manage mode before each showing through <see cref="Use"/>).
    internal static PickerWindow Manage(string title, ActionsWindowService bar)
    {
        var window = new PickerWindow { Title = title };
        window.ManageBar.DataContext = bar;
        window.ManageBar.IsVisible = true;
        window.WireKeys();
        return window;
    }

    /// The model the window shows when it opens next; its keycaps follow every save of the bindings until <see cref="Leave"/>.
    internal void Use(PickerModel model)
    {
        _model = model;
        if (_keys is not { } keys) return;
        model.UseKeys(keys);
        keys.Registry.BindingsSaved += model.RefreshKeys;
    }

    /// Lets go of the model (the window closed, or was hidden to be shown again), so nothing of it runs meanwhile.
    internal void Leave()
    {
        Menu.Release();
        if (_keys is { } keys)
        {
            if (_model is not null) keys.Registry.BindingsSaved -= _model.RefreshKeys;
            keys.Registry.Hold(false);
        }
        _model = null;
    }

    /// Opens the window over <paramref name="owner"/> and returns the chosen entry and settings, or null when cancelled.
    /// <param name="clock">Started at the click that opens it (the timing log); started here when null.</param>
    public static Task<PickResult?> ChooseAsync(Window owner, string title, PickerModel model, Stopwatch? clock = null)
    {
        var window = new PickerWindow { Title = title };
        UiTiming.WhenOpened(window, "Choose", clock ?? Stopwatch.StartNew());
        window.Use(model);
        window.WireKeys();
        // Over a topmost owner (the tray panel) it must be topmost too, or it sits behind it, looks inactive and cannot be dragged.
        window.Topmost = owner.Topmost;
        window.Opened += (_, _) => window.Activate();
        return window.ShowDialog<PickResult?>(owner);
    }

    /// The keys part: the other keys are held from Windows while this window is in front and its search box or a hotkey box has the keys,
    /// so a pressed key reaches it (<see cref="Use"/> gives the model the keys).
    private void WireKeys()
    {
        if (_keys is not { } keys) return;
        void Update(object? sender, EventArgs e) =>
            keys.Registry.Hold(IsActive && FocusManager?.GetFocusedElement() is TextBox box && (box == Menu.SearchBox || HotkeyCapture.IsHotkeyBox(box)));
        AddHandler(GotFocusEvent, Update);
        Activated += Update;
        Deactivated += Update;
    }

    /// What a card's pill (<paramref name="startInSettings"/> false) or cogwheel (true) does: opens the window over the card's window;
    /// the choice, and every live change to the current action's settings, go to the card. Focus then returns to
    /// <paramref name="returnFocusTo"/>, or to <paramref name="picker"/> when that is gone (the new action has no settings).
    public static Task ChooseForAsync(Control row, bool startInSettings, Control returnFocusTo, Control picker) =>
        row.DataContext is IPickerCard card ? ChooseForAsync(row, card, startInSettings, returnFocusTo, picker) : Task.CompletedTask;

    /// The same for <paramref name="card"/>, opened over the window <paramref name="at"/> is in (a control of a pop-down's owner, since a pop-down is not a window).
    public static async Task ChooseForAsync(Control at, IPickerCard card, bool startInSettings, Control returnFocusTo, Control picker)
    {
        if (TopLevel.GetTopLevel(at) is not Window owner) return;
        var clock = Stopwatch.StartNew();
        var model = card.MenuFor(startInSettings);
        model.Target = card.Label;
        model.Live += card.Pick;
        if (await ChooseAsync(owner, $"Choose an action for {card.Label}", model, clock) is { } picked) card.Pick(picked);
        model.Live -= card.Pick;
        (returnFocusTo.IsEffectivelyVisible ? returnFocusTo : picker).Focus(NavigationMethod.Directional);
    }
}
