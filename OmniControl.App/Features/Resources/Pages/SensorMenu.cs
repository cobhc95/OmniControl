using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources.Pages;

/// THE temperature sensor chip of a temperature reading (the Readings tab's Hottest sensor card and the CPU and GPU cards' temperature, the
/// Taskbar gadget tab's temperature rows) and its menu (Auto, then every temperature sensor, grouped by hardware); the reading's one choice
/// (<see cref="ResourcesSetup.SensorOf"/>) drives its value everywhere: cards, gadget, tooltips, state and rules.
public sealed class SensorMenu
{
    public const string Title = "Temperature sensor";

    private readonly IReadingList _readings;
    private readonly ResourcesSettings _settings;
    private readonly Reading _reading;
    private readonly string _auto;

    /// <param name="reading">The reading the choice is for (its <see cref="Reading.Sensor"/>).</param>
    /// <param name="auto">The Auto row's words.</param>
    /// <param name="words">The chip's words; null: the chosen sensor's name, else <paramref name="auto"/>.</param>
    public SensorMenu(IReadingList readings, ResourcesSettings settings, Reading reading, string auto, Func<SensorMenu, string>? words, string? tip = null)
    {
        (_readings, _settings, _reading, _auto) = (readings, settings, reading, auto);
        Item = new MenuChoiceItem(Title, () => words?.Invoke(this) ?? Chosen?.Name ?? _auto, Rows) { OpenChanged = readings.ListSensors, Tip = tip };
    }

    /// The card item (its chip text and rows follow <see cref="Refresh"/>).
    public MenuChoiceItem Item { get; }

    /// The chosen sensor when the latest read found it; null on Auto or when it is gone.
    public TemperatureSensor? Chosen => Current is { } read && read.Id == _settings.Setup.SensorOf(_reading.Id) ? read : null;

    /// The sensor the latest read came from (the chosen one, or Auto's pick).
    private TemperatureSensor? Current => _reading.Sensor!(_readings.Latest);

    /// "Auto · Core (Tctl/Tdie)" (the sensor Auto reads now) or "GPU Core · 53 °C" (the chosen one and its value): the gadget rows' chip.
    public string Summary => Chosen is { } chosen ? $"{chosen.Name} · {Reading.Celsius(chosen.Value)}" : Current is { } read ? $"{_auto} · {read.Name}" : _auto;

    /// The chip's tooltip on a card or row that does not name the sensor itself.
    public static string TipFor(Reading reading) => $"{reading.Name}: the sensor it is read from";

    public void Refresh() => Item.Refresh();

    private IReadOnlyList<MenuRowItem> Rows()
    {
        var sensors = _readings.Latest.TemperatureSensors;
        var choice = _settings.Setup.SensorOf(_reading.Id);
        var hottest = sensors.MaxBy(s => s.Value);
        // A stored sensor that is gone reads as Auto, as the sampler does, and the Auto row says so.
        var gone = choice is not null && sensors.Count > 0 && sensors.All(s => s.Id != choice);
        var detail = gone ? "The chosen sensor is not found now" : choice is null && Current is { } read ? read.Name : string.Empty;
        var rows = new List<MenuRowItem> { Row(_auto, choice is null || gone, null, string.Empty, false) with { Detail = detail } };
        foreach (var group in sensors.GroupBy(s => (s.Group, Hardware: s.Group is SensorGroup.Cpu or SensorGroup.Gpu ? s.Hardware : string.Empty))) // drives and board chips share one heading
        {
            rows.Add(MenuRowItem.Header(Heading(group.Key.Group, group.Key.Hardware), ruleAbove: true));
            rows.AddRange(group.Select(s => Row(s.Short, s.Id == choice, s.Id, Reading.Celsius(s.Value), s == hottest)));
        }
        return rows;
    }

    /// "CPU · AMD Ryzen 7 9700X", "GPU · AMD Radeon RX 9070", or just "Storage" / "Motherboard" (their rows say which).
    private static string Heading(SensorGroup group, string hardware) => group switch
    {
        SensorGroup.Cpu => $"CPU · {hardware}",
        SensorGroup.Gpu => $"GPU · {hardware}",
        SensorGroup.Storage => "Storage",
        _ => "Motherboard",
    };

    private MenuRowItem Row(string text, bool on, string? id, string value, bool warm) =>
        new(text, string.Empty, on, string.Empty, new RelayCommand(() => Choose(id)), Trailing: value, TrailingWarm: warm);

    private void Choose(string? id)
    {
        if (_settings.Setup.SensorOf(_reading.Id) == id) return;
        _settings.Setup.SetSensor(_reading.Id, id);
        _settings.Commit();
    }
}
