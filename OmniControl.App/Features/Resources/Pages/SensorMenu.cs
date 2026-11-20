using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources.Pages;

/// The temperature sensor chip of the Hottest sensor card and its menu (Hottest (auto), then every sensor the library reports, grouped by hardware); the choice drives the card, its state and the gadget.
public sealed class SensorMenu
{
    public const string Title = "Temperature sensor";
    private const string Auto = "Hottest (auto)";

    private readonly ResourceMonitor _monitor;
    private readonly ResourcesSettings _settings;

    /// <param name="listing">The menu opened (true) or closed: the sampler lists the sensors only while it is open.</param>
    public SensorMenu(ResourceMonitor monitor, ResourcesSettings settings, Action<bool> listing)
    {
        (_monitor, _settings) = (monitor, settings);
        Item = new MenuChoiceItem(Title, () => _monitor.Latest.ChosenSensor ?? Auto, Rows) { OpenChanged = listing };
    }

    /// The card item (its chip text and rows follow <see cref="Refresh"/>).
    public MenuChoiceItem Item { get; }

    public void Refresh() => Item.Refresh();

    private IReadOnlyList<MenuRowItem> Rows()
    {
        var sensors = _monitor.Latest.TemperatureSensors;
        var choice = _settings.Setup.Sensor;
        var hottest = sensors.MaxBy(s => s.Value);
        // A stored sensor that is gone reads as Auto, as the sampler does.
        var rows = new List<MenuRowItem> { Row(Auto, choice is null || (sensors.Count > 0 && sensors.All(s => s.Id != choice)), null, string.Empty, false) };
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
        new(text, string.Empty, on, text, new RelayCommand(() => Choose(id)), Trailing: value, TrailingWarm: warm);

    private void Choose(string? id)
    {
        if (_settings.Setup.Sensor == id) return;
        _settings.Setup.Sensor = id;
        _settings.Commit();
    }
}
