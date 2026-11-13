using OmniControl.App.Picker;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Mouse;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// One input's half-width card (a mouse button, or a trackpad gesture): its action, that action's settings, and the text it needs (a
/// shortcut or a program path). A trackpad gesture's card can be unavailable (no precision touchpad) and says when Windows also acts on it.
/// </summary>
public sealed partial class MouseButtonItem : ObservableObject, IEditorItem, IHalfWidth, IPickerCard
{
    private readonly Func<IReadOnlyList<PickChoice>> _choiceSource;
    private IReadOnlyList<PickChoice> _choices;
    private readonly Func<string> _read;
    private readonly Action<string> _write;
    private Dictionary<string, string> _values = new();
    private bool _loading;

    public MouseButton Button { get; }

    public string Label { get; }

    /// <summary>True for a moment after the real button is pressed, so the card lights up.</summary>
    [ObservableProperty] private bool _isFlashing;

    private int _flashId;

    /// <summary>Lights the card for <see cref="Timing.MouseFlash"/>; a press during the flash restarts it.</summary>
    public async void Flash()
    {
        var mine = ++_flashId;
        IsFlashing = true;
        await Task.Delay(Timing.MouseFlash);
        if (mine == _flashId) IsFlashing = false;
    }

    /// <summary>Two buttons share a row.</summary>
    public bool Half => true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected), nameof(Summary), nameof(NeedsParameter), nameof(Hint), nameof(IsCustom), nameof(HasOptions), nameof(ShowsWindowsAlso))]
    private PickChoice _choice;

    /// <summary>Greyed out with the reason when the input can't be seen now (a trackpad gesture without a precision touchpad).</summary>
    public Availability Availability { get; } = new();

    /// <summary>True when Windows itself also acts on this input (a trackpad gesture Windows' touchpad settings use); the page keeps it current.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWindowsAlso))]
    private bool _windowsAlso;

    /// <summary>The card's tag: only once the input has an action of its own, since at "Default" Windows doing its own thing is what is wanted.</summary>
    public bool ShowsWindowsAlso => WindowsAlso && IsCustom;

    public const string WindowsAlsoText = "Windows also does this";

    public const string WindowsAlsoDetail =
        "Windows still runs its own action for this gesture (switching apps or desktops, or zooming), so both happen. Turn it off in Windows touchpad settings.";

    [ObservableProperty] private string _parameter = string.Empty;

    /// <summary>The values of the action's settings (only those the user changed from the default).</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>The name of the chosen action, as shown on the card.</summary>
    public string Selected => Choice.Name;

    /// <summary>What the card's pill says: "Default" for an untouched button, otherwise the action's name.</summary>
    public string Summary => IsCustom ? Choice.Name : "Default";

    public bool NeedsParameter => Choice.ParamHint is not null;
    public string Hint => Choice.ParamHint ?? string.Empty;

    /// <summary>True when the action has settings (the card shows a cogwheel).</summary>
    public bool HasOptions => Choice.HasOptions;

    /// <summary>True when the button has been given an action (its pill is tinted); false while it behaves as Windows normally does.</summary>
    public bool IsCustom => Choice.Spec != MouseActionCatalog.Default;

    /// <param name="choices">The action list as it is now (it grows as features register OmniControl actions).</param>
    /// <param name="read">The stored action text for this button right now.</param>
    /// <param name="write">Stores a new action text.</param>
    public MouseButtonItem(MouseButton button, string label, Func<IReadOnlyList<PickChoice>> choices, Func<string> read, Action<string> write)
    {
        Button = button;
        Label = label;
        _choiceSource = choices;
        _choices = choices();
        _read = read;
        _write = write;
        _choice = _choices[0];
        Reload();
    }

    /// <summary>Shows what is stored now (after the layer or the action list changed), without writing anything back.</summary>
    public void Reload()
    {
        _loading = true;
        _choices = _choiceSource();
        var (action, values) = MouseAction.Split(_read());
        var choice = _choices.FirstOrDefault(c => c.Matches(action)) ?? _choices[0];
        _values = values;
        Choice = choice;
        Parameter = choice.ParameterOf(action);
        _loading = false;
    }

    public PickerModel MenuFor(bool startInSettings) => new(PickChoice.Group(_choices), Choice, Values, startInSettings, Parameter);

    /// <summary>Takes the action and settings chosen in the action window.</summary>
    public void Pick(PickResult pick)
    {
        _values = OptionValues.NonDefault(pick.Choice.Options, pick.Values);
        if (pick.Parameter is not null)
        {
            _loading = true; // saved once below, with the action it belongs to
            Parameter = pick.Parameter;
            _loading = false;
        }
        if (Choice == pick.Choice) Save(); // same action, new settings
        else Choice = pick.Choice;
    }

    partial void OnChoiceChanged(PickChoice value) => Save();

    partial void OnParameterChanged(string value) => Save();

    private void Save()
    {
        if (_loading) return;
        _write(MouseAction.Compose(Choice.WithParameter(Parameter), Choice.Options, _values));
    }
}
