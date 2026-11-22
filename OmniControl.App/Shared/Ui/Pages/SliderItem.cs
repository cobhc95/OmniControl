using System.Globalization;
using System.Windows.Input;

namespace OmniControl.App.Shared.Ui.Pages;

/// A slider (brightness, contrast, EQ band...) that writes to hardware or settings when moved. Defaults to 0-100.
public sealed partial class SliderItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Func<int, OperationResult> _apply;
    private readonly Action<string> _report;
    private bool _ready;

    /// The card's name; it may change while the card is shown (the OSD placement window's offsets are named after the anchor).
    [ObservableProperty] private string _label;

    /// An icon id of the icon library drawn before the label (Other PCs' mirrored cards); null: no icon, the card looks as without.
    public string? Icon { get; init; }

    /// A pick-one chip drawn at the left of the slider inside the same card (Other PCs' Sound: the output beside the volume); null: none, the card looks as without.
    public ChoiceItem? Lead { get; init; }

    public bool HasLead => Lead is not null;

    /// State buttons on a second line of the card under a thin line, side by side at equal width (Other PCs' Sound: Sound and Mic mute under the volume); empty: no second line.
    public IReadOnlyList<StateButtonItem> Extras { get; init; } = Array.Empty<StateButtonItem>();

    public bool HasExtras => Extras.Count > 0;

    /// A muted line under the card's controls; null: none.
    public string? Note { get; init; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// The master cogwheel (Button.pill.cog) at the end of the card, opening the card's further settings (the OSD's Duration); null: none.
    public ICommand? Cog { get; init; }

    /// What the cogwheel opens, as its tooltip.
    public string? CogTip { get; init; }

    public bool HasCog => Cog is not null;

    /// How many decimals the value has: <see cref="Value"/>, <see cref="Minimum"/>, <see cref="Maximum"/> and <see cref="ResetValue"/> then count
    /// in tenths (1) or hundredths (2), and the value is shown and typed as the real number (the OSD's 0.5-10 s). 0: whole numbers, the usual case.
    public int Decimals { get; init; }
    public int Minimum { get; }
    public int Maximum { get; }
    public string Unit { get; }

    /// Where a double-click on the slider puts it: the given default, else 0 for a signed range, else the top.
    /// <remarks>It may change while the card is shown: a monitor control whose standard gives no default returns to the value the monitor had when it was read.</remarks>
    [ObservableProperty] private int _resetValue;

    /// Greyed out, with a reason, while the control cannot be used (a monitor that does not report it).
    public Availability Availability { get; } = new();

    /// An on/off that belongs with this slider, shown at the end of the same line after a divider (Gestures: "Show margins").
    public ToggleItem? Companion { get; init; }

    /// True: the card takes half the width (label, slider and value on one line, see <see cref="HalfPanel"/>). A companion is not shown on a half card.
    public bool Half { get; init; }

    /// True: the master knob (Controls/Knob) replaces the slider, its value words under it, and the name wraps onto two lines (Other PCs' mirrored cards). A companion is not shown.
    public bool AsKnob { get; init; }

    /// Which of the card's layouts the view shows: full width, full width with the companion switch, half width, or the knob.
    public bool IsPlain => !AsKnob && !Half && Companion is null;
    public bool HasCompanionLine => !AsKnob && !Half && Companion is not null;
    public bool IsHalfSlider => !AsKnob && Half;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    private int _value;

    /// The value in words instead of the number and unit (App settings' Accent intensity: "Windows", "Softer 30"); null: the number.
    public Func<int, string>? Words { get; init; }

    public string ValueText => Words is { } words ? words(Value) : Minimum < 0 && Value > 0 ? $"+{Number}{Unit}" : $"{Number}{Unit}";

    private string Number => Decimals == 0
        ? Value.ToString(CultureInfo.CurrentCulture)
        : (Value / Math.Pow(10, Decimals)).ToString("F" + Decimals, CultureInfo.CurrentCulture);

    public SliderItem(string label, int initial, Func<int, OperationResult> apply, Action<string> report,
        int minimum = 0, int maximum = 100, string unit = "", int? defaultValue = null)
    {
        _label = label;
        _apply = apply;
        _report = report;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        ResetValue = defaultValue ?? (minimum < 0 ? 0 : maximum);
        _value = initial;
        _ready = true;
    }

    /// A slider made from its search card: label and muted line are the card's.
    public SliderItem(SearchCard card, int initial, Func<int, OperationResult> apply, Action<string> report,
        int minimum = 0, int maximum = 100, string unit = "", int? defaultValue = null)
        : this(card.Label, initial, apply, report, minimum, maximum, unit, defaultValue) => Note = card.Note;

    public IEnumerable<string> SearchTexts => new[] { Note, CogTip }.Where(t => !string.IsNullOrEmpty(t))!;

    /// Moves the slider to a value read from elsewhere without writing it back.
    public void Load(int value)
    {
        _ready = false;
        Value = value;
        _ready = true;
    }

    partial void OnValueChanged(int value)
    {
        if (!_ready) return;
        var r = _apply(value);
        if (!r.Success) _report(r.Message);
    }
}
