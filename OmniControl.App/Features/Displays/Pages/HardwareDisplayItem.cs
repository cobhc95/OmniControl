using OmniControl.App.Features.Osd;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays.Pages;

/// One display's own controls (DDC/CI, or a laptop's panel) as groups of cards, and the operations the page's shared hotkey actions run on it.
/// The cards exist at once, greyed out; <see cref="Start"/> asks the display what it supports on a worker thread, and the answer fills in
/// the values and enables the cards it has. The display is only asked once: the answer stays with it until the display is reopened
/// (Refresh, or a change of the attached displays). After the answer the page shows the cards the display has; the ones it does not
/// report wait, greyed and with the reason, in a second group that is collapsed.
internal sealed partial class HardwareDisplayItem
{
    private const string Checking = "Reading the monitor...";
    private const string NotAnswering = "No DDC/CI answer. Turn it on in the monitor's menu, then Refresh.";
    private const string NotListed = "Values not reported";
    private const string NoRange = "Reported, but without a range to set";
    private const string Waiting = "...";
    private const string UnavailableName = "Not available on ";

    private readonly IHardwareAccess _access;
    private readonly LevelStepper _levels;
    private readonly IOsd _osd;
    private readonly SystemScreens _screens;
    private readonly Action<string> _report;
    private readonly Action _groupsChanged;
    private readonly Action _valuesChanged;
    private readonly Dictionary<HardwareLevel, SliderItem> _sliders = new();
    private readonly Dictionary<HardwareChoice, ChoiceItem> _choices = new();
    private readonly Dictionary<HardwareChoice, IReadOnlyList<VcpChoice>> _values = new();
    private readonly DisplayOrientationCard _orientation;
    private MonitorCapabilities? _capabilities;
    private ItemGroup? _main;
    private bool? _screenOn;
    private bool _disposed;

    /// "laptop" for the built-in panel, otherwise Windows' name for the display: the id of its hotkey device.
    public string Id { get; }
    public string Name { get; }
    public bool IsLaptopPanel => _access.IsLaptopPanel;

    /// What the page shows for this display: its group, and after the probe a collapsed group of what it does not report.
    public IReadOnlyList<ItemGroup> Groups { get; private set; }

    /// True once the monitor has said it takes DDC power commands (VCP 0xD6). Never the built-in panel: Windows has no per-screen way to switch it off and on again.
    public bool HasScreenPower { get; private set; }

    /// Whether this screen is on: off while Windows says the screens are all off (whoever did it), else what the monitor said when it was read
    /// (VCP 0xD6) and the last command sent from here; null while not known. A press of the monitor's own button is seen at the next read.
    public bool? ScreenOn => HasScreenPower ? (_screens.IsOff ? false : _screenOn) : null;

    /// Every card this display could have, whether it reports the control or not.
    public IReadOnlyList<IEditorItem> Cards { get; }

    /// The cards the display has (none until it has been read).
    public IEnumerable<IEditorItem> AvailableCards => Cards.Where(c => AvailabilityOf(c).IsAvailable);

    /// <param name="groupsChanged">Called on the UI thread when <see cref="Groups"/> was replaced (after the display was read).</param>
    /// <param name="valuesChanged">Called on the UI thread when a value the display shows changed (a card was used or a hotkey stepped it).</param>
    /// <param name="orientation">The display's Orientation card, which Windows (not DDC/CI) answers, so it is available whatever the monitor reports.</param>
    public HardwareDisplayItem(string id, string name, IHardwareAccess access, DisplayOrientationCard orientation, LevelStepper levels, IOsd osd,
        SystemScreens screens, Action<string> report, Action groupsChanged, Action valuesChanged)
    {
        (Id, Name, _access, _orientation, _levels, _osd, _screens, _report, _groupsChanged, _valuesChanged) =
            (id, name, access, orientation, levels, osd, screens, report, groupsChanged, valuesChanged);

        var cards = new List<IEditorItem>();
        foreach (var level in DisplayControls.Hardware)
        {
            var spec = level.Spec;
            var slider = new SliderItem(spec.Label, spec.Default, v => Set(level, v), report, spec.Min, spec.Max, defaultValue: spec.Default) { Half = true };
            _sliders[level] = slider;
            cards.Add(slider);
        }
        foreach (var choice in DisplayControls.HardwareChoices)
        {
            var card = new ChoiceItem(choice.Feature.Label, new[] { Waiting }, Waiting, picked => Pick(choice, picked)) { Half = true };
            _choices[choice] = card;
            cards.Add(card);
        }
        cards.Add(_orientation.Card);
        Cards = cards;

        foreach (var card in Cards) AvailabilityOf(card).MakeUnavailable(Checking);
        Groups = new[] { new ItemGroup(GroupKey, name, Cards.Cast<object>().ToList()) { Summary = Waiting } };
    }

    private string GroupKey => $"Displays/Hardware/{Id}";

    /// The level as its card shows it, or null while the display has not reported it (the page's states read this).
    public int? LevelOf(HardwareLevel level) => _sliders.TryGetValue(level, out var slider) && slider.Availability.IsAvailable ? slider.Value : null;

    /// Moves the level's card to this value, which writes it to the monitor as a move of the card does (the page's states set it); nothing while the display has not reported it.
    public void SetLevel(HardwareLevel level, int value)
    {
        if (_sliders.TryGetValue(level, out var slider) && slider.Availability.IsAvailable) slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    private static Availability AvailabilityOf(IEditorItem card) => card switch
    {
        SliderItem s => s.Availability,
        ChoiceItem c => c.Availability,
        ButtonItem b => b.Availability,
        GalleryItem g => g.Availability,
        _ => throw new ArgumentException("Not a card with an availability.", nameof(card)),
    };

    /// Asks the display what it supports, off the UI thread, and fills the cards in.
    public void Start()
    {
        _orientation.Start();
        Task.Run(() =>
        {
            var capabilities = _access.Probe();
            Dispatcher.UIThread.Post(() => ShowCapabilities(capabilities));
        });
    }

    /// Stops the display from touching the page when its answer arrives late.
    public void Dispose()
    {
        _disposed = true;
        _orientation.Dispose();
    }

    private void ShowCapabilities(MonitorCapabilities capabilities)
    {
        if (_disposed) return;
        _capabilities = capabilities;
        var reason = capabilities.Answers ? _access.UnsupportedReason : NotAnswering;

        foreach (var (level, slider) in _sliders)
        {
            if (capabilities.Level(level.Code) is { } value)
            {
                slider.Load(value.Percent);
                if (level.ResetToReading) slider.ResetValue = value.Percent;
                _levels.Remember(KeyOf(level), value.Percent);
                slider.Availability.MakeAvailable();
            }
            else slider.Availability.MakeUnavailable(capabilities.Has(level.Code) ? NoRange : reason);
        }

        foreach (var (choice, card) in _choices)
        {
            _values[choice] = capabilities.ChoicesOf(choice.Feature.Code);
            ShowChoice(card, capabilities, choice.Feature.Code, _values[choice], reason);
        }
        HasScreenPower = !IsLaptopPanel && capabilities.Has(VcpCode.PowerMode);
        _screenOn = HasScreenPower ? !VcpCode.Power.IsOff(capabilities.Features[VcpCode.PowerMode].Current) : null;

        ShowGroups();
    }

    /// The cards the display has go in its group; the others go, greyed, in a collapsed group of their own.
    private void ShowGroups()
    {
        var shown = AvailableCards.Cast<object>().ToList();
        var hidden = Cards.Where(c => !AvailabilityOf(c).IsAvailable).Cast<object>().ToList();
        _main = new ItemGroup(GroupKey, Name, shown) { Summary = $"{shown.Count} available", Tip = HasScreenPower ? null : NoPowerTip };
        if (HasScreenPower) _main.UseSwitch(ScreenPowerSwitch.One(new ScreenPowerSwitch.Screen(() => ScreenOn, SetScreenOn)));
        var groups = new List<ItemGroup> { _main };
        if (hidden.Count > 0)
            groups.Add(new ItemGroup($"{GroupKey}/unavailable", UnavailableName + Name, hidden) { Summary = $"{hidden.Count} not reported" });
        Groups = groups;
        _groupsChanged();
    }

    private static void ShowChoice(ChoiceItem card, MonitorCapabilities capabilities, byte code, IReadOnlyList<VcpChoice> choices, string reason)
    {
        if (!capabilities.Has(code)) card.Availability.MakeUnavailable(reason);
        else if (choices.Count == 0) card.Availability.MakeUnavailable(NotListed);
        else
        {
            var current = choices.FirstOrDefault(c => c.Value == capabilities.Features[code].Current) ?? choices[0];
            card.Load(choices.Select(c => c.Name).ToList(), current.Name);
            card.Availability.MakeAvailable();
        }
    }

    // ---------------------------------------------------------------- writing

    private string KeyOf(HardwareLevel level) => $"{Id}:{level.Code}";

    /// A slider moved: remember it as the level the next hotkey step starts from and write the newest value in the background.
    private OperationResult Set(HardwareLevel level, int percent)
    {
        _levels.Set(KeyOf(level), percent, p => Write(level, p));
        _valuesChanged();
        return OperationResult.Ok();
    }

    /// Writes a level (worker thread). A failure is shown in the status line.
    private void Write(HardwareLevel level, int percent)
    {
        if (IsLaptopPanel && level.Spec.Osd == OsdService.BrightnessElement)
            _osd.NoteOwnChange(level.Spec.Osd); // the brightness watcher would otherwise pop up the same change again
        Fail(_access.WritePercent(level.Code, percent));
    }

    private void Fail(OperationResult result)
    {
        if (!result.Success) _report(result.Message);
    }

    private void WriteRaw(byte code, uint value) => Task.Run(() => Fail(_access.WriteRaw(code, value)));

    private void Pick(HardwareChoice choice, string name)
    {
        if (_values.GetValueOrDefault(choice)?.FirstOrDefault(v => v.Name == name) is { } value) WriteRaw(choice.Feature.Code, value.Value);
        _valuesChanged();
    }

    private string NoPowerTip => IsLaptopPanel
        ? "No power switch for this screen: Windows has no way to turn the built-in panel off and on on its own. Use Turn off displays for all screens."
        : "No power switch for this screen: the monitor does not report DDC power control.";

    /// Switches this one screen off (it sleeps; its button or input wakes it) or on again, over DDC power; nothing when the monitor has no
    /// power control. While Windows has all screens off a monitor gets no signal to answer DDC, so "on" wakes the screens instead and the
    /// monitors are read again when Windows says they are on. A command the monitor refused is taken back and reported.
    public void SetScreenOn(bool on)
    {
        if (!HasScreenPower) return;
        var before = _screenOn;
        _screenOn = on;
        if (on && _screens.IsOff) _screens.WakeUp();
        else Task.Run(() =>
        {
            var result = _access.WriteRaw(VcpCode.PowerMode, on ? VcpCode.Power.On : VcpCode.Power.Off);
            if (result.Success) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                _screenOn = before;
                _report(result.Message);
                Shown();
            });
        });
        Shown();
    }

    /// Windows said the screens went off, came on or dimmed: the switch follows at once, and a screen that is on is read again (it may have been switched off by itself before).
    public void ScreensChanged()
    {
        _main?.RefreshMaster();
        if (HasScreenPower && !_screens.IsOff) Task.Run(ReadScreenPower);
    }

    /// Asks the monitor its power mode (VCP 0xD6) and shows the answer; an unanswered read keeps what is known (worker thread).
    private void ReadScreenPower()
    {
        if (_access.ReadRaw(VcpCode.PowerMode) is not { } mode) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _screenOn = !VcpCode.Power.IsOff(mode);
            Shown();
        });
    }

    private void Shown()
    {
        _main?.RefreshMaster();
        _valuesChanged();
    }

    /// Changes a level by <paramref name="delta"/> percent from the level last set or read and returns the new level (null when the display
    /// does not report it). <paramref name="corrected"/> hears of a correction after a re-read. Call off the UI thread.
    public int? Step(HardwareLevel level, int delta, Action<int>? corrected)
    {
        var now = _levels.Step(KeyOf(level), delta, () => _access.ReadPercent(level.Code), p => Write(level, p), corrected);
        if (now is { } value) Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _sliders[level].Load(value);
            _valuesChanged();
        });
        return now;
    }

    // ---------------------------------------------------------------- what the shared hotkey actions do (UI thread unless noted)

    /// True once the display has said it has this level; the hotkey actions skip a display that has not.
    public bool Supports(HardwareLevel level) => _sliders[level].Availability.IsAvailable;

    /// Selects the next value of a choice (the next colour preset or input source...); nothing when the display lists none.
    public void NextChoice(HardwareChoice choice) => SelectNext(_choices[choice]);

    /// Turns the display to this orientation; nothing while Windows has not said how it is turned.
    public void Rotate(DisplayOrientation orientation) => _orientation.Choose(orientation);

    /// Choosing the option runs the card's own pick handler, which writes it.
    private static void SelectNext(ChoiceItem card)
    {
        var names = card.Options;
        if (!card.Availability.IsAvailable || names.Count == 0) return;
        card.Selected = names[Wrap.Index(names.ToList().IndexOf(card.Selected) + 1, names.Count)];
    }
}
