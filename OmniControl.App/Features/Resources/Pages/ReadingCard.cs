namespace OmniControl.App.Features.Resources.Pages;

/// A Live readings card (half width): icon and name, the value, then a sparkline of recent values (or a muted note, the hottest sensor's name) and one fact tag.
public sealed partial class ReadingCard : ObservableObject, IEditorItem, IHalfWidth
{
    public ReadingCard(string label, string icon, double maximum = 0) => (Label, Icon, Maximum) = (label, icon, maximum);

    public string Label { get; }
    public string Icon { get; }
    public bool Half => true;

    /// The network adapter chip shown where the fact tag would be (the Download and Upload cards); null: the card has none.
    public MenuChoiceItem? Adapter { get; init; }

    public bool HasAdapter => Adapter is not null;

    /// The sparkline's top (0: the largest value shown).
    public double Maximum { get; }

    [ObservableProperty] private string _value = Reading.Unknown;
    [ObservableProperty] private IReadOnlyList<double> _history = Array.Empty<double>();
    [ObservableProperty] private TagItem? _tag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    /// True: the card shows its note where the sparkline would be.
    public bool HasNote => Note.Length > 0;
}
