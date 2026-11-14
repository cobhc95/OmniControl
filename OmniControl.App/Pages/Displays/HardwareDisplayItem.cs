using Avalonia.Threading;
using OmniControl.App.Osd;
using OmniControl.App.Services;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// One display's own controls (DDC/CI, or a laptop's panel) as a group of cards, and the operations the page's shared hotkey actions run on it.
/// The cards exist at once, greyed out; <see cref="Start"/> asks the display what it supports on a worker thread, and the answer fills in
/// the values and enables the cards it has. The display is only asked
/// once: the answer stays with it until the display is reopened (Refresh, or a change of the attached displays).
/// </summary>
internal sealed class HardwareDisplayItem
{
    private const string Checking = "Reading the monitor...";
    private const string NotAnswering = "No DDC/CI answer. Turn it on in the monitor's menu, then Refresh.";
    private const string NotListed = "Values not reported";
    private const string Waiting = "...";

    private readonly IHardwareAccess _access;
    private readonly LevelStepper _levels;
    private readonly OsdService _osd;
    private readonly Action<string> _report;
    private readonly Dictionary<HardwareLevel, SliderItem> _sliders = new();
    private readonly ChoiceItem _preset, _input;
    private readonly ButtonItem _power;
    private IReadOnlyList<VcpChoice> _presets = Array.Empty<VcpChoice>(), _inputs = Array.Empty<VcpChoice>();
    private bool _disposed;

    /// <summary>"laptop" for the built-in panel, otherwise Windows' name for the display: the id of its hotkey device.</summary>
    public string Id { get; }
    public string Name { get; }
    public bool IsLaptopPanel => _access.IsLaptopPanel;
    public ItemGroup Group { get; }
    public IReadOnlyList<IEditorItem> Cards { get; }

    public HardwareDisplayItem(string id, string name, IHardwareAccess access, LevelStepper levels, OsdService osd, Action<string> report)
    {
        (Id, Name, _access, _levels, _osd, _report) = (id, name, access, levels, osd, report);

        var cards = new List<IEditorItem>();
        foreach (var level in DisplayControls.Hardware)
        {
            var spec = level.Spec;
            var slider = new SliderItem(spec.Label, spec.Default, v => Set(level, v), report, spec.Min, spec.Max, defaultValue: spec.Default) { Half = true };
            _sliders[level] = slider;
            cards.Add(slider);
        }
        _preset = new ChoiceItem("Colour preset", new[] { Waiting }, Waiting, PickPreset) { Half = true };
        _input = new ChoiceItem("Input source", new[] { Waiting }, Waiting, PickInput) { Half = true };
        _power = new ButtonItem("Screen power", "Turn off",
            "Wake it with its button or input", PowerOff) { Half = true };
        cards.AddRange(new IEditorItem[] { _preset, _input, _power });
        Cards = cards;

        foreach (var card in Cards) AvailabilityOf(card).MakeUnavailable(Checking);
        Group = new ItemGroup($"Displays/Hardware/{id}", name, Cards.Cast<object>().ToList());
    }

    private static Availability AvailabilityOf(IEditorItem card) => card switch
    {
        SliderItem s => s.Availability,
        ChoiceItem c => c.Availability,
        ButtonItem b => b.Availability,
        _ => throw new ArgumentException("Not a card with an availability.", nameof(card)),
    };

    /// <summary>Asks the display what it supports, off the UI thread, and fills the cards in.</summary>
    public void Start() => Task.Run(() =>
    {
        var capabilities = _access.Probe();
        Dispatcher.UIThread.Post(() => ShowCapabilities(capabilities));
    });

    /// <summary>Stops the display from touching the page when its answer arrives late. </summary>
    public void Dispose() => _disposed = true;

    private void ShowCapabilities(MonitorCapabilities capabilities)
    {
        if (_disposed) return;
        var reason = capabilities.Answers ? _access.UnsupportedReason : NotAnswering;

        foreach (var (level, slider) in _sliders)
        {
            if (capabilities.Level(level.Code) is { } value)
            {
                slider.Load(value.Percent);
                _levels.Remember(KeyOf(level), value.Percent);
                slider.Availability.MakeAvailable();
            }
            else slider.Availability.MakeUnavailable(reason);
        }

        _presets = capabilities.Presets;
        _inputs = capabilities.Inputs;
        ShowChoice(_preset, capabilities, VcpCode.ColorPreset, _presets, reason);
        ShowChoice(_input, capabilities, VcpCode.InputSource, _inputs, reason);
        if (capabilities.Has(VcpCode.PowerMode)) _power.Availability.MakeAvailable();
        else _power.Availability.MakeUnavailable(reason);

        Group.Summary = $"{Cards.Count(c => AvailabilityOf(c).IsAvailable)} of {Cards.Count} available";
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

    /// <summary>A slider moved: remember it as the level the next hotkey step starts from and write the newest value in the background.</summary>
    private OperationResult Set(HardwareLevel level, int percent)
    {
        _levels.Set(KeyOf(level), percent, p => Write(level, p));
        return OperationResult.Ok();
    }

    /// <summary>Writes a level (worker thread). A failure is shown in the status line.</summary>
    private void Write(HardwareLevel level, int percent)
    {
        if (IsLaptopPanel && level.Spec.Osd == OsdService.BrightnessElement)
            _osd.NoteOwnChange(level.Spec.Osd); // the brightness watcher would otherwise pop up the same change again
        Fail(_access.WritePercent(level.Code, percent));
    }

    private void Fail(OperationResult result)
    {
        if (!result.Success) Dispatcher.UIThread.Post(() => _report(result.Message));
    }

    private void WriteRaw(byte code, uint value) => Task.Run(() => Fail(_access.WriteRaw(code, value)));

    private void PickPreset(string name)
    {
        if (_presets.FirstOrDefault(p => p.Name == name) is { } preset) WriteRaw(VcpCode.ColorPreset, preset.Value);
    }

    private void PickInput(string name)
    {
        if (_inputs.FirstOrDefault(i => i.Name == name) is { } input) WriteRaw(VcpCode.InputSource, input.Value);
    }

    private void PowerOff() => WriteRaw(VcpCode.PowerMode, VcpCode.Power.Off);

    /// <summary>
    /// Changes a level by <paramref name="delta"/> percent from the level last set or read and returns the new level (null when the display
    /// does not report it). <paramref name="corrected"/> hears of a correction after a re-read. Call off the UI thread.
    /// </summary>
    public int? Step(HardwareLevel level, int delta, Action<int>? corrected)
    {
        var now = _levels.Step(KeyOf(level), delta, () => _access.ReadPercent(level.Code), p => Write(level, p), corrected);
        if (now is { } value) Dispatcher.UIThread.Post(() => { if (!_disposed) _sliders[level].Load(value); });
        return now;
    }

    // ---------------------------------------------------------------- what the shared hotkey actions do (UI thread unless noted)

    /// <summary>True once the display has said it has this level; the hotkey actions skip a display that has not.</summary>
    public bool Supports(HardwareLevel level) => _sliders[level].Availability.IsAvailable;

    /// <summary>Selects the next colour preset; nothing when the display lists none.</summary>
    public void NextPreset() => SelectNext(_preset);

    /// <summary>Switches to the next input source; nothing when the display lists none.</summary>
    public void NextInput() => SelectNext(_input);

    /// <summary>Turns the screen off; nothing when the display has no power control.</summary>
    public void TurnOff()
    {
        if (_power.Availability.IsAvailable) PowerOff();
    }

    /// <summary>Choosing the option runs the card's own pick handler, which writes it.</summary>
    private static void SelectNext(ChoiceItem card)
    {
        var names = card.Options;
        if (!card.Availability.IsAvailable || names.Count == 0) return;
        card.Selected = names[Wrap.Index(names.ToList().IndexOf(card.Selected) + 1, names.Count)];
    }
}
