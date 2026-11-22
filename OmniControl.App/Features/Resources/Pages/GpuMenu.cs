using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources.Pages;

/// THE graphics adapter chip of the GPU usage and GPU temperature readings (the Taskbar gadget tab's rows, while the PC has more than one
/// adapter) and its menu: Auto (the busiest, the temperature ranking), then each adapter. The reading's one choice
/// (<see cref="ResourcesSetup.GpuOf"/>) drives its value everywhere, as <see cref="SensorMenu"/> does; an adapter that is gone reads as Auto.
public sealed class GpuMenu
{
    public const string Title = "Graphics card";

    private readonly IReadingList _readings;
    private readonly ResourcesSettings _settings;
    private readonly string _readingId;

    public GpuMenu(IReadingList readings, ResourcesSettings settings, Reading reading)
    {
        (_readings, _settings, _readingId) = (readings, settings, reading.Id);
        Item = new MenuChoiceItem(Title, () => Chosen?.Name ?? "Auto", Rows)
        {
            Tip = reading.Kind == ReadingKind.Temperature ? $"{reading.Name}: the graphics card it is read from while its sensor is on Auto" : $"{reading.Name}: the graphics card it is read from",
        };
    }

    public MenuChoiceItem Item { get; }

    private GraphicsAdapter? Chosen => _readings.Gpus.FirstOrDefault(g => g.Id == _settings.Setup.GpuOf(_readingId));

    public void Refresh() => Item.Refresh();

    private IReadOnlyList<MenuRowItem> Rows() => _readings.Gpus.Select(g => Row(g.Name, Chosen == g, g.Id))
        .Prepend(Row("Auto", Chosen is null, null)).ToList();

    private MenuRowItem Row(string text, bool on, string? id) => new(text, string.Empty, on, string.Empty, new RelayCommand(() => Choose(id)));

    private void Choose(string? id)
    {
        if (_settings.Setup.GpuOf(_readingId) == id) return;
        _settings.Setup.SetGpu(_readingId, id);
        _settings.Commit();
    }
}
