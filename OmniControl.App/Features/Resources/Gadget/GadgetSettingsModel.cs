using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Icons;
using OmniControl.App.Features.Resources.Weather;

namespace OmniControl.App.Features.Resources.Gadget;

/// The Resources page's Taskbar gadget tab: "Show on taskbar", Position and Font, layout tiles, readings list and the Weather, Size and spacing, Numbers and units,
/// Hover details and Clicks groups; it holds the readings and redraws only while it is on screen (<see cref="SetOnScreen"/>).
public sealed class GadgetSettingsModel : ObservableObject, IDisposable
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
        Display = new GadgetDisplayCards(settings, actions, () => preview.Ink, () => preview.Background, chooseAction);
        Weather = new WeatherCards(settings, weather);
        Hover = new GadgetHoverCards(settings);
        Position = new GalleryItem("Position", GadgetSettings.PositionNames, (int)Look.Position, i => preview.SetPosition((GadgetPosition)i))
        {
            Half = true, Tip = "Right: left of the tray icons. Left: after the Widgets button, or after the app buttons when they sit at the left.",
        };
        Font = new GadgetFontCard(() => Look.Font, preview.SetFont);
        LabelChoice = new ChoiceItem("Labels", LabelWords.Select(l => l.Words).ToList(), MixedLabels,
            words => SetLabels(LabelWords.FirstOrDefault(l => l.Words == words).Style));
        Rows = new ObservableCollection<GadgetRowItem>(Listed.Select(Row));
        settings.Changed += OnChanged;
        preview.Readings.ReadingsChanged += OnChanged;
        OnChanged();
    }

    /// The rows the list shows: every reading's and the weather's; a disk that is not connected keeps its row, unlisted, until it is back.
    private IReadOnlyList<GadgetReading> Listed => Look.Readings.Where(r => r.Id == GadgetSettings.WeatherId || _preview.Readings.Find(r.Id) is not null).ToList();

    private GadgetSettings Look => _settings.Setup.Gadget;

    /// "Show on taskbar": the gadget on or off.
    public bool Shown
    {
        get => _preview.IsShown;
        set => _preview.SetShown(value);
    }

    /// "Position" beside "Show on taskbar": the gadget at the taskbar's right or left end (one card, so the card keys and wheel work).
    public GalleryItem Position { get; }
    public IReadOnlyList<object> PositionCard => new object[] { Position };

    /// "Font" beside "Position": the gadget's font, one for every layout.
    public GadgetFontCard Font { get; }
    public IReadOnlyList<object> FontCard => new object[] { Font.Item };

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

    /// The settings global search offers (Position, Font and the cards of the Weather, Size and spacing, Numbers and units, Hover details and Clicks groups).
    public IEnumerable<IEditorItem> SearchItems => new[] { Weather.Group, Display.Layout, Display.Numbers, Hover.Group, Display.Behaviour }
        .SelectMany(g => g.Items).OfType<IEditorItem>().Prepend(Font.Item).Prepend(Position);

    /// The gadget's background (the taskbar's colour when see-through) behind the label editor's line and the layout tiles (kept, so a read draws on the same brush).
    private SolidColorBrush? _background;

    /// The tab came on or left the screen: the readings are held (every part, for the layout tiles) and the tiles and lines follow them only while it shows.
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

    private GadgetRowItem Row(GadgetReading entry) => new(entry, _settings, _preview.Readings, _chooseIcon, () => _preview.Ink);

    private void SetLabels(LabelStyle style)
    {
        GadgetLayouts.Labels(Look, style, _preview.Readings.Find);
        _settings.Commit();
    }

    /// Puts the dragged rows at their new place (<see cref="ListDragReorder"/>): in front of the <paramref name="at"/>-th listed row that stays,
    /// so an unlisted disk's row keeps its place among the rest.
    public void Move(IReadOnlyList<int> rows, int at)
    {
        var all = Look.Readings;
        var moved = rows.Where(i => i >= 0 && i < Rows.Count).Select(i => Rows[i].Entry).ToList();
        var staying = all.Except(moved).ToList();
        var before = Rows.Select(r => r.Entry).Except(moved).ElementAtOrDefault(at);
        Look.Readings = Reorder.Moved(all, moved.Select(e => all.IndexOf(e)).ToList(), before is null ? staying.Count : staying.IndexOf(before));
        _settings.Commit();
    }

    // A change here, from the gadget's right-click menu, a layout, a preset, an import or a disk that came or went: the switch, rows and cards show it again.
    private void OnChanged()
    {
        OnPropertyChanged(nameof(Shown));
        Position.Load((int)Look.Position);
        Font.Item.Refresh();
        var listed = Listed;
        ListDiff.Apply(Rows, listed.Select(r => Rows.FirstOrDefault(row => row.Entry == r && ReferenceEquals(row.Reading, _preview.Readings.Find(r.Id))) ?? Row(r)).ToList(), row => row.Entry.Id);
        foreach (var row in Rows) row.Load();
        ReadingsGroup.Summary = $"{listed.Count(r => r.Shown)} of {listed.Count} shown";
        var styles = Rows.Where(row => row.Reading is not null).Select(row => GadgetLabel.StyleOf(row.Entry.Label, row.Reading!)).Distinct().ToList();
        var one = styles.Count == 1 ? LabelWords.FirstOrDefault(l => l.Style == styles[0]).Words : null;
        LabelChoice.Load(LabelChoice.Options, one ?? MixedLabels);
        Display.Load();
        Hover.Load();
    }

    /// Draws the layout tiles, the label editor's line and the rows' sensor chips with the latest reading (every read while the tab is on screen).
    private void Redraw()
    {
        var look = Look;
        var cells = _preview.Cells(look, sampleUnknown: true);
        var fill = TaskbarGadget.FillOf(look.Background, _preview.Background) ?? _preview.Background; // see-through shows the taskbar behind it
        if (_background?.Color != fill) _background = new SolidColorBrush(fill);
        var line = GadgetLayout.Of(look) with { Lines = 1 };
        foreach (var row in Rows)
        {
            (row.Line, row.LineLayout, row.LineBackground) = (cells, line, _background);
            row.Sensor?.Refresh();
        }
        Gallery.Draw(layout => _preview.Cells(layout, sampleUnknown: true), _background, look.Font);
    }

    public void Dispose()
    {
        SetOnScreen(false);
        Gallery.Dispose();
        _settings.Changed -= OnChanged;
        _preview.Readings.ReadingsChanged -= OnChanged;
        _layoutChoice.Dispose();
        Display.Dispose();
        Weather.Dispose();
        Hover.Dispose();
    }
}
