using OmniControl.App.Features.Icons;

namespace OmniControl.App.Features.Resources.Gadget;

/// One icon cell of the label editor's grids: selected while the label holds it.
public sealed partial class LabelIconItem : ObservableObject
{
    public LabelIconItem(string id, IRelayCommand pick) => (Id, Pick) = (id, pick);

    public string Id { get; }

    public string Name => IconLibrary.Find(Id)?.Name ?? Id;

    public IRelayCommand Pick { get; }

    [ObservableProperty] private bool _isSelected;

    /// The colour the label's icons would take (a brush), or unset to keep the cell's own colour.
    [ObservableProperty] private object? _ink = AvaloniaProperty.UnsetValue;
}

/// One icon grid of the label editor: its heading, its cells and how many cells a line holds (every line full or nearly, never one lone icon).
public sealed record LabelIconGroup(string Name, IReadOnlyList<LabelIconItem> Icons)
{
    /// The most cells a line of the editor's pop-down holds (its width over the icon cell's).
    private const int MaxColumns = 8;

    public int Columns => (Icons.Count + Lines - 1) / Lines;

    private int Lines => (Icons.Count + MaxColumns - 1) / MaxColumns;
}

/// One row of the Taskbar gadget tab's readings list (the weather too): shown, its label and editor, its warn point and colour; saved at once.
public sealed partial class GadgetRowItem : ObservableObject
{
    /// The icon grids the label editor offers (up/down pairs side by side); "+ icon" opens THE icon picker for any other.
    private static readonly (string Name, string[] Ids)[] IconGroups =
    {
        ("Network icons", new[] { "arrow-up", "arrow-down", "upload", "download", "cloud-upload", "cloud-download", "circle-arrow-up", "circle-arrow-down",
            "chevron-up", "chevron-down", "arrow-big-up", "arrow-big-down", "arrow-bar-up", "arrow-bar-to-down", "wifi", "network" }),
        ("Temperature icons", new[] { "temperature", "temperature-celsius", "temperature-plus", "temperature-sun", "temperature-snow", "flame", "droplet", "gauge" }),
        ("Component icons", new[] { "cpu", "cpu-2", "device-desktop-analytics", "server", "disc", "database", "battery", "fan" }),
        ("Memory icons", new[] { "device-sd-card", "memory-stick", "brain" }),
    };

    private const double HiddenOpacity = 0.6;
    private readonly ResourcesSettings _settings;
    private readonly Reading? _reading;
    private readonly Func<Visual, IconRequest, Task<IconPick?>> _chooseIcon;
    private readonly Func<Color> _ink;
    private bool _loading;

    /// <param name="ink">The taskbar's own text colour (the "" colour).</param>
    public GadgetRowItem(GadgetReading entry, ResourcesSettings settings, Func<Visual, IconRequest, Task<IconPick?>> chooseIcon, Func<Color> ink)
    {
        (Entry, _settings, _reading, _chooseIcon, _ink) = (entry, settings, Reading.Find(entry.Id), chooseIcon, ink);
        Colours = GadgetSettings.Colours.Select(c => new SwatchItem(new SolidColorBrush(TaskbarGadget.ColourOf(c, ink())), new RelayCommand(() => Set(e => e.Colour = c)))).ToList();
        // The grid holding the reading's best suggestion comes first (network for speeds, components for loads, thermometers for temperatures).
        var suggested = _reading?.SuggestedIcons ?? Array.Empty<string>();
        int Rank(string[] ids) => suggested.Select((id, i) => ids.Contains(id) ? i : int.MaxValue).DefaultIfEmpty(int.MaxValue).Min();
        IconGrids = IconGroups.OrderBy(g => Rank(g.Ids))
            .Select(g => new LabelIconGroup(g.Name, g.Ids.Select(id => new LabelIconItem(id, new RelayCommand(() => ToggleIcon(id)))).ToList())).ToList();
        (IconColours, AllIconColours) = (Dots(() => SelectedIcon), Dots(() => null));
        var kind = _reading?.Kind == ReadingKind.Temperature ? "Apply to all temperatures" : "Apply to all of this kind";
        QuickChips = _reading is null
            ? new[] { Quick("No label", LabelStyle.None) }
            : new[] { Quick("Text only", LabelStyle.Text), Quick("Icon only", LabelStyle.Icons), Quick("Icon + text", LabelStyle.Both), Quick("No label", LabelStyle.None),
                new ChipItem(kind, new RelayCommand(ApplyToKind), null) };
        Load();
    }

    private ChipItem Quick(string name, LabelStyle style) =>
        new(name, new RelayCommand(() => Label = _reading is null ? string.Empty : GadgetLabel.Of(style, _reading)), null) { Key = style.ToString() };

    public GadgetReading Entry { get; }
    public string Name => _reading?.Name ?? (Entry.Id == GadgetSettings.WeatherId ? "Weather" : Entry.Id);
    public string EditorTitle => $"Label for {Name}: type text, insert icons anywhere";
    public int WarnMax => _reading?.Max ?? 0;
    public bool HasWarn => _reading is not null;
    public IReadOnlyList<SwatchItem> Colours { get; }
    public IReadOnlyList<LabelIconGroup> IconGrids { get; }

    /// The icon colour dots, "follow the text colour" first: the label editor's (the selected icon, else every icon) and the Readings table's (every icon).
    public IReadOnlyList<SwatchItem> IconColours { get; }
    public IReadOnlyList<SwatchItem> AllIconColours { get; }

    private IReadOnlyList<SwatchItem> Dots(Func<int?> at) =>
        GadgetLabel.IconColours.Cast<string?>().Prepend(null).Select(n => new SwatchItem(GadgetLabel.InkOf(n) is { } c ? new SolidColorBrush(c) : null,
            new RelayCommand(() => ColourIcons(n, at())), n ?? "Follow the text colour")).ToList();
    public IReadOnlyList<ChipItem> QuickChips { get; }
    public int GapMax => GadgetSettings.MaxGapAfter;

    [ObservableProperty] private bool _shown;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private double _warnAt;
    [ObservableProperty] private string _warnText = Reading.Unknown;
    [ObservableProperty] private IBrush? _colour;
    [ObservableProperty] private double _gapAfter;
    [ObservableProperty] private string _gapText = string.Empty;
    [ObservableProperty] private double _opacity = 1;

    /// The gadget's cells in one line, for the editor's preview (set by the window while it is open).
    [ObservableProperty] private IReadOnlyList<GadgetCell>? _line;
    [ObservableProperty] private GadgetLayout? _lineLayout;
    [ObservableProperty] private IBrush? _lineBackground;

    /// The label box should select this (start, length; length 0 = only the caret): after an inserted icon, or an icon token picked or recoloured.
    public event Action<int, int>? SelectionMoved;

    /// Where the selected icon token starts in the label (clicked into in the label box), or null: the icon colour then goes to every icon.
    [ObservableProperty] private int? _selectedIcon;

    /// The Readings table's icon colour dot: the label's one icon colour (null: none or mixed), whether its icons follow the text colour, whether it has any.
    [ObservableProperty] private IBrush? _iconDot;
    [ObservableProperty] private bool _iconFollows;
    [ObservableProperty] private bool _hasIcon;

    public bool HasLabel => Label.Length > 0;

    /// Shows the entry as saved now.
    public void Load()
    {
        _loading = true;
        (Shown, Label, WarnAt, GapAfter) = (Entry.Shown, Entry.Label, Entry.WarnAt ?? 0, Entry.GapAfter);
        WarnText = Entry.WarnAt is { } at && _reading is not null ? _reading.UnitWords(at) : Reading.Unknown;
        GapText = $"{Entry.GapAfter:0} px";
        Colour = new SolidColorBrush(TaskbarGadget.ColourOf(Entry.Colour, _ink()));
        Opacity = Entry.Shown ? 1 : HiddenOpacity;
        _loading = false;
        ShowLabelState();
    }

    partial void OnShownChanged(bool value) => Set(e => e.Shown = value);
    partial void OnWarnAtChanged(double value) => Set(e => e.WarnAt = value > 0 ? value : null);
    partial void OnGapAfterChanged(double value) => Set(e => e.GapAfter = Math.Clamp(Math.Round(value), 0, GadgetSettings.MaxGapAfter));

    partial void OnLabelChanged(string value)
    {
        OnPropertyChanged(nameof(HasLabel));
        ShowLabelState();
        Set(e => e.Label = value);
    }

    /// Puts the icon at <see cref="Caret"/> when the label lacks it, else takes it out (an icon cell's click).
    private void ToggleIcon(string id)
    {
        if (GadgetLabel.Holds(Label, id))
        {
            Label = GadgetLabel.Without(Label, id);
            return;
        }
        Insert(GadgetLabel.Token(id));
    }

    /// Where the label box's caret is (the view keeps it current), so an icon goes in where the user is typing.
    public int Caret { get; set; }

    private void Insert(string token)
    {
        var at = Math.Clamp(Caret, 0, Label.Length);
        Label = Label.Insert(at, token);
        Caret = at + token.Length;
        SelectionMoved?.Invoke(Caret, 0);
    }

    /// The label box's selection moved: a caret inside an icon token selects that token whole (the box is asked to select it), a selection
    /// that is exactly one token keeps it, anything else selects none.
    public void PickToken(int from, int to)
    {
        var (lo, hi) = (Math.Min(from, to), Math.Max(from, to));
        var token = GadgetLabel.TokenAt(Label, lo == hi ? lo : lo + 1);
        SelectedIcon = token is { } t && (lo == hi || (t.Start == lo && t.Start + t.Length == hi)) ? t.Start : null;
        if (lo == hi && token is { } whole) SelectionMoved?.Invoke(whole.Start, whole.Length);
    }

    /// An icon colour dot: the icon token starting at <paramref name="start"/> (null: every icon) in this colour (null: the text's colour).
    private void ColourIcons(string? colour, int? start)
    {
        Label = GadgetLabel.Recolour(Label, start, colour);
        if (start is { } s && GadgetLabel.TokenAt(Label, s + 1) is { } t) SelectionMoved?.Invoke(t.Start, t.Length);
    }

    partial void OnSelectedIconChanged(int? value) => ShowInk();

    /// The icon grids draw in the colour a pick would get: the selected icon's, or the one all the label's icons share.
    private void ShowInk()
    {
        object ink = GadgetLabel.InkOf(GadgetLabel.ColourOf(Label, SelectedIcon)) is { } c ? new SolidColorBrush(c) : AvaloniaProperty.UnsetValue;
        foreach (var icon in IconGrids.SelectMany(g => g.Icons)) icon.Ink = ink;
    }

    /// "+ icon": THE icon picker over the window that holds <paramref name="from"/>; the chosen icon goes in at the caret.
    public async Task ChooseIconAsync(Visual from)
    {
        if (await _chooseIcon(from, new IconRequest(Name, null, IconLibrary.Suggestions(_reading?.SuggestedIcons, _reading?.Icon, Name))) is { Value: { } icon }) Insert(GadgetLabel.Token(icon));
    }

    /// The same label style (or, for a label of the user's own, the same words) on every reading of this kind.
    private void ApplyToKind()
    {
        if (_reading is null) return;
        var style = GadgetLabel.StyleOf(Label, _reading);
        foreach (var row in _settings.Setup.Gadget.Readings)
            if (Reading.Find(row.Id) is { } other && other.Kind == _reading.Kind) row.Label = style is { } s ? GadgetLabel.Of(s, other) : Label;
        _settings.Commit();
    }

    private void ShowLabelState()
    {
        foreach (var icon in IconGrids.SelectMany(g => g.Icons)) icon.IsSelected = GadgetLabel.Holds(Label, icon.Id);
        var style = _reading is null ? (Label.Length == 0 ? LabelStyle.None : (LabelStyle?)null) : GadgetLabel.StyleOf(Label, _reading);
        foreach (var chip in QuickChips) chip.IsSelected = chip.Key == style?.ToString();
        var icons = GadgetLabel.Parts(Label).Where(p => p.Icon is not null).ToList();
        HasIcon = icons.Count > 0;
        IconFollows = HasIcon && icons.All(p => p.Colour is null);
        IconDot = GadgetLabel.InkOf(GadgetLabel.ColourOf(Label, null)) is { } c ? new SolidColorBrush(c) : null;
        ShowInk();
    }

    private void Set(Action<GadgetReading> change)
    {
        if (_loading) return;
        change(Entry);
        _settings.Commit();
    }
}
