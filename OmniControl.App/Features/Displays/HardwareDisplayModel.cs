using OmniControl.App.Features.Displays.Pages;
using OmniControl.App.Features.Osd;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays;

/// One display's own controls (DDC/CI, or a laptop's panel): each level and choice with its value and whether the display has it, its
/// screen power and orientation, and what the actions, states, presets and cards do to them. Exists without the page. <see cref="Start"/>
/// asks the display what it supports on a worker thread; the answer stays until the display is reopened (Refresh, or a change of the
/// attached displays). Used on the UI thread unless noted.
internal sealed partial class HardwareDisplayModel
{
    private const string Checking = "Reading the monitor...";
    private const string NotAnswering = "No DDC/CI answer. Turn it on in the monitor's menu, then Refresh.";
    private const string NotListed = "Values not reported";
    private const string NoRange = "Reported, but without a range to set";

    private readonly IHardwareAccess _access;
    private readonly LevelStepper _levels;
    private readonly IOsd _osd;
    private readonly SystemScreens _screens;
    private readonly Action<string> _report;
    private readonly Action _read;
    private readonly Dictionary<HardwareLevel, LevelState> _levelStates = new();
    private readonly Dictionary<HardwareChoice, ChoiceState> _choiceStates = new();
    private MonitorCapabilities? _capabilities;
    private bool? _screenOn;
    private bool _disposed;

    /// "laptop" for the built-in panel, otherwise Windows' name for the display: the id of its hotkey device.
    public string Id { get; }
    public string Name { get; }
    public bool IsLaptopPanel => _access.IsLaptopPanel;
    public DisplayOrientationModel Orientation { get; }

    /// True once the display has answered what it supports.
    public bool IsRead => _capabilities is not null;

    /// True once the monitor has said it takes DDC power commands (VCP 0xD6). Never the built-in panel: Windows has no per-screen way to switch it off and on again.
    public bool HasScreenPower { get; private set; }

    /// Whether this screen is on: off while Windows says the screens are all off (whoever did it), else what the monitor said when it was read
    /// (VCP 0xD6) and the last command sent from here; null while not known. A press of the monitor's own button is seen at the next read.
    public bool? ScreenOn => HasScreenPower ? (_screens.IsOff ? false : _screenOn) : null;

    /// A value or the screen power changed (a card, an action, a state or a preset; or a read).
    public event Action? Changed;

    /// <param name="read">Told when the display has answered what it supports.</param>
    /// <param name="orientation">The display's orientation, which Windows (not DDC/CI) answers, so it is available whatever the monitor reports.</param>
    public HardwareDisplayModel(string id, string name, IHardwareAccess access, DisplayOrientationModel orientation, LevelStepper levels, IOsd osd,
        SystemScreens screens, Action<string> report, Action read)
    {
        (Id, Name, _access, Orientation, _levels, _osd, _screens, _report, _read) = (id, name, access, orientation, levels, osd, screens, report, read);
        foreach (var level in DisplayControls.Hardware) _levelStates[level] = new LevelState(level.Spec.Default, level.Spec.Default, Checking);
        foreach (var choice in DisplayControls.HardwareChoices) _choiceStates[choice] = new ChoiceState { Reason = Checking };
    }

    public LevelState Level(HardwareLevel level) => _levelStates[level];

    public ChoiceState Choice(HardwareChoice choice) => _choiceStates[choice];

    /// The labels of the controls the display has (none until it has been read).
    public IEnumerable<string> AvailableLabels =>
        DisplayControls.Hardware.Where(l => _levelStates[l].IsAvailable).Select(l => l.Spec.Label)
            .Concat(DisplayControls.HardwareChoices.Where(c => _choiceStates[c].IsAvailable).Select(c => c.Feature.Label))
            .Concat(Orientation.State.IsAvailable ? new[] { DisplayOrientationModel.Label } : Array.Empty<string>());

    /// The level, or null while the display has not reported it (the page's states read this).
    public int? LevelOf(HardwareLevel level) => _levelStates[level] is { IsAvailable: true } state ? state.Value : null;

    /// True once the display has said it has this level; the hotkey actions skip a display that has not.
    public bool Supports(HardwareLevel level) => _levelStates[level].IsAvailable;

    /// Asks the display what it supports, off the UI thread.
    public void Start()
    {
        Orientation.Start();
        Guard.OffUi("Displays: reading a monitor's features", () =>
        {
            var capabilities = _access.Probe();
            Dispatcher.UIThread.Post(() => ShowCapabilities(capabilities));
        });
    }

    /// Stops the display from touching anything when its answer arrives late.
    public void Dispose()
    {
        _disposed = true;
        Orientation.Dispose();
    }

    private void ShowCapabilities(MonitorCapabilities capabilities)
    {
        if (_disposed) return;
        _capabilities = capabilities;
        var reason = capabilities.Answers ? _access.UnsupportedReason : NotAnswering;

        foreach (var (level, state) in _levelStates)
        {
            if (capabilities.Level(level.Code) is { } value)
            {
                (state.Value, state.Reason) = (value.Percent, null);
                if (level.ResetToReading) state.ResetValue = value.Percent;
                _levels.Remember(KeyOf(level), value.Percent);
            }
            else state.Reason = capabilities.Has(level.Code) ? NoRange : reason;
        }

        foreach (var (choice, state) in _choiceStates)
        {
            var code = choice.Feature.Code;
            state.Values = capabilities.ChoicesOf(code);
            if (!capabilities.Has(code)) state.Reason = reason;
            else if (state.Values.Count == 0) state.Reason = NotListed;
            else
            {
                state.Selected = (state.Values.FirstOrDefault(c => c.Value == capabilities.Features[code].Current) ?? state.Values[0]).Name;
                state.Reason = null;
            }
        }
        HasScreenPower = !IsLaptopPanel && capabilities.Has(VcpCode.PowerMode);
        _screenOn = HasScreenPower ? !VcpCode.Power.IsOff(capabilities.Features[VcpCode.PowerMode].Current) : null;
        _read();
    }

    // ---------------------------------------------------------------- writing

    private string KeyOf(HardwareLevel level) => $"{Id}:{level.Code}";

    /// Sets the level (a card or a state), clamped; nothing while the display has not reported it. Remembered as the level the next
    /// hotkey step starts from; the newest value is written in the background.
    public void Set(HardwareLevel level, int percent)
    {
        var state = _levelStates[level];
        if (!state.IsAvailable) return;
        state.Value = Math.Clamp(percent, level.Spec.Min, level.Spec.Max);
        _levels.Set(KeyOf(level), state.Value, p => Write(level, p));
        Changed?.Invoke();
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

    /// Chooses a value of a choice by its name (the card) and writes it.
    public void Pick(HardwareChoice choice, string name)
    {
        var state = _choiceStates[choice];
        state.Selected = name;
        if (state.Values.FirstOrDefault(v => v.Name == name) is { } value)
            Guard.OffUi("Displays: writing a monitor setting", () => Fail(_access.WriteRaw(choice.Feature.Code, value.Value)));
        Changed?.Invoke();
    }

    /// Selects the next value of a choice (the next colour preset or input source...); nothing when the display lists none.
    public void NextChoice(HardwareChoice choice)
    {
        var state = _choiceStates[choice];
        var names = state.Names;
        if (!state.IsAvailable || names.Count == 0) return;
        Pick(choice, names[Wrap.Index(names.ToList().IndexOf(state.Selected) + 1, names.Count)]);
    }

    /// Turns the display to this orientation; nothing while Windows has not said how it is turned.
    public void Rotate(DisplayOrientation orientation) => Orientation.Choose(orientation);

    /// Changes a level by <paramref name="delta"/> percent from the level last set or read and returns the new level (null when the display
    /// does not report it). <paramref name="corrected"/> hears of a correction after a re-read. Call off the UI thread.
    public int? Step(HardwareLevel level, int delta, Action<int>? corrected)
    {
        var now = _levels.Step(KeyOf(level), delta, () => _access.ReadPercent(level.Code), p => Write(level, p), corrected);
        if (now is { } value) Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _levelStates[level].Value = value;
            Changed?.Invoke();
        });
        return now;
    }

    // ---------------------------------------------------------------- screen power

    /// Switches this one screen off (it sleeps; its button or input wakes it) or on again, over DDC power; nothing when the monitor has no
    /// power control. While Windows has all screens off a monitor gets no signal to answer DDC, so "on" wakes the screens instead and the
    /// monitors are read again when Windows says they are on. A command the monitor refused is taken back and reported.
    public void SetScreenOn(bool on)
    {
        if (!HasScreenPower) return;
        var before = _screenOn;
        _screenOn = on;
        if (on && _screens.IsOff) _screens.WakeUp();
        else Guard.OffUi("Displays: monitor power", () =>
        {
            var result = _access.WriteRaw(VcpCode.PowerMode, on ? VcpCode.Power.On : VcpCode.Power.Off);
            if (result.Success) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                _screenOn = before;
                _report(result.Message);
                Changed?.Invoke();
            });
        });
        Changed?.Invoke();
    }

    /// Windows said the screens went off, came on or dimmed: a screen that is on is read again (it may have been switched off by itself before).
    public void ScreensChanged()
    {
        if (HasScreenPower && !_screens.IsOff) Guard.OffUi("Displays: reading monitor power", ReadScreenPower);
    }

    /// Asks the monitor its power mode (VCP 0xD6) and shows the answer; an unanswered read keeps what is known (worker thread).
    private void ReadScreenPower()
    {
        if (_access.ReadRaw(VcpCode.PowerMode) is not { } mode) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _screenOn = !VcpCode.Power.IsOff(mode);
            Changed?.Invoke();
        });
    }
}
