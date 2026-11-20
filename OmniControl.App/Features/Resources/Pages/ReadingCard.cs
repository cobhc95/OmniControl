namespace OmniControl.App.Features.Resources.Pages;

/// A Live readings card (half width): icon and name, the value, then a sparkline of recent values (or a muted note, the hottest sensor's hardware) and one fact tag or chip.
public sealed partial class ReadingCard : ObservableObject, IEditorItem, IHalfWidth
{
    public ReadingCard(string label, string icon, double maximum = 0, double minimum = 0) => (_label, Icon, Maximum, Minimum) = (label, icon, maximum, minimum);

    public string Icon { get; }
    public bool Half => true;

    /// The network adapter chip shown where the fact tag would be (the Download and Upload cards); null: the card has none.
    public MenuChoiceItem? Adapter { get; init; }

    public bool HasAdapter => Adapter is not null;

    /// The temperature sensor chip shown where the fact tag would be (the Hottest sensor card); that card draws its sparkline along its foot.
    public MenuChoiceItem? Sensor { get; init; }

    public bool HasSensor => Sensor is not null;

    /// The sparkline's range: its bottom, and its top (0: the largest value shown).
    public double Minimum { get; }
    public double Maximum { get; }

    /// From this value the sparkline's line turns to the caution colour (NaN: never).
    public double WarnAt { get; init; } = double.NaN;

    [ObservableProperty] private string _label;
    [ObservableProperty] private string _value = Reading.Unknown;
    [ObservableProperty] private IReadOnlyList<double> _history = Array.Empty<double>();
    [ObservableProperty] private TagItem? _tag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNote), nameof(InlineGraph))] private string _note = string.Empty;

    /// True: the card shows its note where the sparkline would be.
    public bool HasNote => Note.Length > 0;

    /// The sparkline sits beside the value's row (every card but the hottest sensor's, whose graph is along its foot).
    public bool InlineGraph => !HasNote && !HasSensor;
}
