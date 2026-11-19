using System.Globalization;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.App.Features.Resources.Gadget;

/// The Size and spacing, Numbers and units and Clicks groups of the Taskbar gadget tab (half cards, master sliders); every change is saved and shown at once.
public sealed class GadgetDisplayCards : IDisposable
{
    private static readonly string[] UnitWords = { "Auto", "K/s", "M/s" }, CountWords = { "Bytes", "Bits" };
    private static readonly (OddReading Odd, string Words)[] OddWords =
        { (OddReading.Centre, "Centre it"), (OddReading.Larger, "Larger, both lines"), (OddReading.FillTop, "Fill top first") };

    /// The unit speeds and loads can show, as a chip (on = the unit is shown); temperatures have a chip menu of their styles.
    private static readonly (string Words, Func<GadgetSettings, bool> Get, Action<GadgetSettings, bool> Set)[] UnitToggles =
    {
        ("Speeds (K/s)", g => g.ShowSpeedUnits, (g, on) => g.ShowSpeedUnits = on),
        ("Percent (%)", g => g.ShowPercentUnits, (g, on) => g.ShowPercentUnits = on),
    };

    /// The temperature chip's words: the style's name after "Temperatures", as the chip shows only the chosen words.
    private static readonly IReadOnlyList<string> TemperatureWords = Reading.SignWords.Select(w => "Temperatures: " + w).ToList();

    private readonly ResourcesSettings _settings;
    private readonly IActionCatalogue _actions;
    private readonly Func<PickerModel, string, Task<PickResult?>> _choose;
    private readonly SliderItem _lines, _size, _columnGap, _lineGap, _spaceToTray, _refresh;
    private readonly MenuChoiceItem _odd;
    private readonly GalleryItem _colour;
    private readonly ToggleItem _outline;
    private readonly ChoiceItem _units, _counts, _temperatures;
    private readonly IReadOnlyList<ChipItem> _quick, _unitChips;
    private readonly ButtonItem _click, _doubleClick;

    /// <param name="ink">The taskbar's own text colour (the "Taskbar text" swatch).</param>
    /// <param name="choose">Opens the Choose-the-action window with a title.</param>
    public GadgetDisplayCards(ResourcesSettings settings, IActionCatalogue actions, Func<Color> ink, Func<PickerModel, string, Task<PickResult?>> choose)
    {
        (_settings, _actions, _choose) = (settings, actions, choose);
        var (look, defaults) = (Look, new GadgetSettings());
        _lines = Number("Lines", look.Lines, GadgetSettings.MinLines, GadgetSettings.MaxLines, string.Empty, defaults.Lines, (g, v) => g.Lines = v);
        _odd = new MenuChoiceItem("Odd reading", OddText, OddRows) { Half = true, Tip = "Where a reading that does not fill the last column goes" };
        _size = Number("Text size", (int)Math.Round(look.TextSize), GadgetSettings.MinTextSize, GadgetSettings.MaxTextSize, " pt", (int)defaults.TextSize, (g, v) => g.TextSize = v);
        _outline = new ToggleItem("Outline on hover", look.HoverOutline, on => Change(g => g.HoverOutline = on)) { Half = true, Tip = "A dashed edge around the gadget while the pointer is over it" };
        _columnGap = Number("Space between columns", (int)look.ColumnGap, 0, GadgetSettings.MaxColumnGap, " px", (int)defaults.ColumnGap, (g, v) => g.ColumnGap = v);
        _lineGap = Number("Space between lines", (int)look.LineGap, 0, GadgetSettings.MaxLineGap, " px", (int)defaults.LineGap, (g, v) => g.LineGap = v);
        _spaceToTray = Number("Space to the tray", (int)look.SpaceToTray, GadgetSettings.MinSpaceToTray, GadgetSettings.MaxSpaceToTray, " px", (int)defaults.SpaceToTray, (g, v) => g.SpaceToTray = v,
            "Below 0 tucks into Windows' space by the ^ button");
        _colour = new GalleryItem("Text colour", GadgetSettings.Colours.Select(c => c.Length == 0 ? "Taskbar text" : c).ToList(), ColourIndex(look.TextColour),
            () => Swatches.Build(GadgetSettings.Colours.Select(c => TaskbarGadget.ColourOf(c, ink())).ToList(), ColourIndex(Look.TextColour)),
            i => Change(g => g.TextColour = GadgetSettings.Colours[i]));
        Layout = Group("Resources/GadgetLayout", "Size and spacing", _lines, _odd, _size, _outline, _columnGap, _lineGap, _spaceToTray, _colour);

        _units = new ChoiceItem("Speed units", UnitWords, UnitWords[(int)look.Units], u => Change(g => g.Units = (SpeedUnit)Array.IndexOf(UnitWords, u))) { Half = true };
        _counts = new ChoiceItem("Bits or bytes", CountWords, CountWords[look.Bits ? 1 : 0], b => Change(g => g.Bits = b == CountWords[1])) { Half = true };
        _temperatures = new ChoiceItem("Temperatures", TemperatureWords, TemperatureWords[(int)look.Temperatures], w => Change(g => g.Temperatures = (TemperatureSign)TemperatureWords.ToList().IndexOf(w)));
        _unitChips = UnitToggles.Select(u => new ChipItem(u.Words, new RelayCommand(() => Change(g => u.Set(g, !u.Get(g)))), null) { Key = u.Words }).ToList();
        _refresh = Number("Refresh every", RefreshMs, (int)Timing.ResourcesGadgetReadMin.TotalMilliseconds, (int)Timing.ResourcesGadgetReadMax.TotalMilliseconds, " ms",
            (int)Timing.ResourcesGadgetReadEvery.TotalMilliseconds, (g, v) => g.ReadEverySeconds = v / 1000d);
        _quick = Timing.ResourcesGadgetQuickReads.Select(t => new ChipItem(Words(t), new RelayCommand(() => Change(g => g.ReadEverySeconds = t.TotalSeconds)), null)
            { Key = t.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) }).ToList();
        Numbers = Group("Resources/GadgetNumbers", "Numbers and units", _units, _counts, new ChoiceChipsItem("Show units", _unitChips) { Menu = _temperatures }, _refresh,
            new ChoiceChipsItem("Quick", _quick) { Half = true, Note = $"Under {Words(Timing.ResourcesGadgetReadCostly)} uses more CPU." });

        _click = new ButtonItem("Click", TitleOf(look.ClickAction), "The action a click on the gadget runs", () => _ = PickAsync("Choose the gadget's click action", (g, id) => g.ClickAction = id)) { Half = true };
        _doubleClick = new ButtonItem("Double-click", TitleOf(look.DoubleClickAction), "The action a double-click on the gadget runs",
            () => _ = PickAsync("Choose the gadget's double-click action", (g, id) => g.DoubleClickAction = id)) { Half = true };
        Behaviour = Group("Resources/GadgetBehaviour", "Clicks", _click, _doubleClick);
        Load();
    }

    private GadgetSettings Look => _settings.Setup.Gadget;

    private int RefreshMs => (int)Math.Round(Look.ReadEverySeconds * 1000);

    public ItemGroup Layout { get; }
    public ItemGroup Numbers { get; }
    public ItemGroup Behaviour { get; }

    private static ItemGroup Group(string key, string name, params object[] items) => new(key, name, items) { ExpandedByDefault = true, Summary = string.Empty };

    private SliderItem Number(string label, int value, int min, int max, string unit, int reset, Action<GadgetSettings, int> set, string? note = null) =>
        new(label, value, v =>
        {
            Change(g => set(g, v));
            return OperationResult.Ok();
        }, _ => { }, min, max, unit, reset) { Half = true, Note = note };

    /// "250 ms", "1 s".
    private static string Words(TimeSpan t) => t.TotalSeconds >= 1 ? $"{t.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture)} s" : $"{t.TotalMilliseconds:0} ms";

    private string OddText() => OddWords.First(o => o.Odd == Look.Odd).Words;

    private IReadOnlyList<MenuRowItem> OddRows() => OddWords.Select(o => new MenuRowItem(o.Words, string.Empty, Look.Odd == o.Odd, string.Empty,
        new RelayCommand(() => Change(g => g.Odd = o.Odd)))).ToList();

    private static int ColourIndex(string stored) => Math.Max(0, GadgetSettings.Colours.ToList().FindIndex(c => string.Equals(c, stored, StringComparison.OrdinalIgnoreCase)));

    private string TitleOf(string actionId) => _actions.Find(actionId)?.Title ?? "Not available";

    private void Change(Action<GadgetSettings> change)
    {
        change(Look);
        _settings.Commit();
    }

    private async Task PickAsync(string title, Action<GadgetSettings, string> set)
    {
        var model = new PickerModel(PickChoice.Group(_actions.Actions.Select(a => MouseActionCatalog.OmniEntry(a, unavailable: a.UnavailableReason))), null, new Dictionary<string, string>());
        if (await _choose(model, title) is { } pick && MouseActionCatalog.OmniIdOf(pick.Choice.Spec) is { } id) Change(g => set(g, id));
    }

    /// Shows the settings as they are now (a change here, from a layout, the right-click menu, a preset or an import).
    public void Load()
    {
        var look = Look;
        _lines.Load(look.Lines);
        _odd.Refresh();
        _size.Load((int)Math.Round(look.TextSize));
        _outline.Load(look.HoverOutline);
        _columnGap.Load((int)look.ColumnGap);
        _lineGap.Load((int)look.LineGap);
        _spaceToTray.Load((int)look.SpaceToTray);
        _colour.Load(ColourIndex(look.TextColour));
        _units.Load(UnitWords, UnitWords[(int)look.Units]);
        _counts.Load(CountWords, CountWords[look.Bits ? 1 : 0]);
        _temperatures.Load(TemperatureWords, TemperatureWords[(int)look.Temperatures]);
        foreach (var chip in _unitChips) chip.IsSelected = UnitToggles.First(u => u.Words == chip.Key).Get(look);
        _refresh.Load(RefreshMs);
        foreach (var chip in _quick) chip.IsSelected = chip.Key == RefreshMs.ToString(CultureInfo.InvariantCulture);
        _click.ButtonText = TitleOf(look.ClickAction);
        _doubleClick.ButtonText = TitleOf(look.DoubleClickAction);
    }

    public void Dispose()
    {
        Layout.Dispose();
        Numbers.Dispose();
        Behaviour.Dispose();
    }
}
