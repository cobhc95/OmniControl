using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Icons;
using OmniControl.App.Features.Resources.Weather;

namespace OmniControl.App.Features.Resources.Gadget;

/// The Resources page's Taskbar gadget tab: preview and "Show on taskbar", layout tiles, readings list and the Weather, Size and spacing, Numbers and units,
/// Hover details and Clicks groups; it holds the readings and redraws only while it is on screen (<see cref="SetOnScreen"/>).
public sealed partial class GadgetSettingsModel : ObservableObject, IDisposable
{
    private static readonly (LabelStyle Style, string Words)[] LabelWords =
        { (LabelStyle.Text, "Labels: Text"), (LabelStyle.Icons, "Labels: Icons"), (LabelStyle.Both, "Labels: Both") };

    /// The Labels chip's words while the readings' labels are not all one style.
    private const string MixedLabels = "Labels: Mixed";

    private readonly ResourcesSettings _settings;
    private readonly IGadgetPreview _preview;
    private readonly ProviderChoice _layoutChoice;
    private readonly Func<Visual, IconRequest, Task<IconPick?>> _chooseIcon;
    private IDisposable? _hold;

    /// <param name="chooseAction">Opens the Choose-the-action window with a title (the click actions).</param>
    public GadgetSettingsModel(ResourcesSettings settings, IActionCatalogue actions, IGadgetPreview preview, GadgetLayouts layouts, Action manageLayouts,
        WeatherService weather, Func<Visual, IconRequest, Task<IconPick?>> chooseIcon, Func<PickerModel, string, Task<PickResult?>> chooseAction)
    {
        (_settings, _preview, _chooseIcon) = (settings, preview, chooseIcon);
        _layoutChoice = new ProviderChoice("Gadget layouts", layouts, manageLayouts, withCustom: true);
        Gallery = new GadgetLayoutGallery(layouts, manageLayouts, () =>
        {
            if (_hold is not null) Redraw();
        });
        Display = new GadgetDisplayCards(settings, actions, () => preview.Ink, chooseAction);
        Weather = new WeatherCards(settings, weather);
        Hover = new GadgetHoverCards(settings);
        LabelChoice = new ChoiceItem("Labels", LabelWords.Select(l => l.Words).ToList(), MixedLabels,
            words => SetLabels(LabelWords.FirstOrDefault(l => l.Words == words).Style));
        Rows = new ObservableCollection<GadgetRowItem>(Look.Readings.Select(Row));
        settings.Changed += OnChanged;
        OnChanged();
    }

    private GadgetSettings Look => _settings.Setup.Gadget;

    /// "Show on taskbar": the gadget on or off.
    public bool Shown
    {
        get => _preview.IsShown;
        set => _preview.SetShown(value);
    }

    public ChoiceItem LayoutChoice => _layoutChoice.Choice;
    /// The Ready-made layouts group's tiles, folded or unfolded in place.
    public GadgetLayoutGallery Gallery { get; }

    /// The What it shows header's "Labels: …" chip: every reading's label in one style.
    public ChoiceItem LabelChoice { get; }
    public ObservableCollection<GadgetRowItem> Rows { get; }
    public GadgetDisplayCards Display { get; }
    public CardGroup LayoutsGroup { get; } = new("Resources/GadgetLayouts", "Ready-made layouts", string.Empty) { ExpandedByDefault = true };

    /// Its summary reads "N of M shown" beside the name.
    public CardGroup ReadingsGroup { get; } = new("Resources/GadgetReadings", "What it shows", string.Empty) { ExpandedByDefault = true, SummaryBesideName = true };
    public WeatherCards Weather { get; }
    public GadgetHoverCards Hover { get; }

    /// The settings global search offers (the cards of the Weather, Size and spacing, Numbers and units, Hover details and Clicks groups).
    public IEnumerable<IEditorItem> SearchItems => new[] { Weather.Group, Display.Layout, Display.Numbers, Hover.Group, Display.Behaviour }
        .SelectMany(g => g.Items).OfType<IEditorItem>();

    [ObservableProperty] private IReadOnlyList<GadgetCell>? _previewCells;
    [ObservableProperty] private GadgetLayout? _previewLayout;
    [ObservableProperty] private IBrush? _previewBackground;

    /// The tab came on or left the screen: the readings are held (every part, for the layout tiles) and the preview follows them only while it shows.
    public void SetOnScreen(bool onScreen)
    {
        if (onScreen == _hold is not null) return;
        if (onScreen)
        {
            _hold = _preview.Hold();
            _preview.Changed += Redraw;
            Redraw();
            return;
        }
        _preview.Changed -= Redraw;
        _hold!.Dispose();
        _hold = null;
    }

    private GadgetRowItem Row(GadgetReading entry) => new(entry, _settings, _chooseIcon, () => _preview.Ink);

    private void SetLabels(LabelStyle style)
    {
        GadgetLayouts.Labels(Look, style);
        _settings.Commit();
    }

    /// Puts the dragged rows at their new place (<see cref="ListDragReorder"/>).
    public void Move(IReadOnlyList<int> rows, int at)
    {
        Look.Readings = Reorder.Moved(Look.Readings, rows, at);
        _settings.Commit();
    }

    // A change here, from the gadget's right-click menu, a layout, a preset or an import: the switch, rows and cards show it again.
    private void OnChanged()
    {
        OnPropertyChanged(nameof(Shown));
        ListDiff.Apply(Rows, Look.Readings.Select(r => Rows.FirstOrDefault(row => row.Entry == r) ?? Row(r)).ToList(), row => row.Entry.Id);
        foreach (var row in Rows) row.Load();
        ReadingsGroup.Summary = $"{Look.Readings.Count(r => r.Shown)} of {Look.Readings.Count} shown";
        var styles = Look.Readings.Where(g => Reading.Find(g.Id) is not null).Select(g => GadgetLabel.StyleOf(g.Label, Reading.Find(g.Id)!)).Distinct().ToList();
        var one = styles.Count == 1 ? LabelWords.FirstOrDefault(l => l.Style == styles[0]).Words : null;
        LabelChoice.Load(LabelChoice.Options, one ?? MixedLabels);
        Display.Load();
        Hover.Load();
    }

    /// Draws the preview, the layout tiles and the label editor's line with the latest reading (every read while the tab is on screen).
    private void Redraw()
    {
        var look = Look;
        PreviewCells = _preview.Cells(look, sampleUnknown: true);
        PreviewLayout = GadgetLayout.Of(look);
        if ((PreviewBackground as ISolidColorBrush)?.Color != _preview.Background) PreviewBackground = new SolidColorBrush(_preview.Background);
        var line = PreviewLayout with { Lines = 1 };
        foreach (var row in Rows) (row.Line, row.LineLayout, row.LineBackground) = (PreviewCells, line, PreviewBackground);
        Gallery.Draw(layout => _preview.Cells(layout, sampleUnknown: true), PreviewBackground);
    }

    public void Dispose()
    {
        SetOnScreen(false);
        Gallery.Dispose();
        _settings.Changed -= OnChanged;
        _layoutChoice.Dispose();
        Display.Dispose();
        Weather.Dispose();
        Hover.Dispose();
    }
}
