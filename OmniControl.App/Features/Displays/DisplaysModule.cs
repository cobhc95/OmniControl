using Avalonia.Controls.Templates;
using OmniControl.App.Features.Displays.Pages;
using OmniControl.Core;

namespace OmniControl.App.Features.Displays;

/// Displays: the monitors' hardware engine, the software adjustments, the displays' service (its cards, actions, states, presets and
/// watches) and the page.
public sealed class DisplaysModule : IFeatureModule
{
    private readonly MonitorEngine _engine = new();
    private readonly SoftwareDisplayService _software = new();
    private FeatureContext? _context;
    private DisplaysService? _service;

    public void Register(FeatureContext context)
    {
        _context = context;
        context.Imports.OnReload(DataStores.Displays, () => _service?.ReloadSettings() ?? false);
        context.Shutdown.Add(ShutdownStage.Pages, "displays", () => _service?.Dispose());
        context.Shutdown.Add(ShutdownStage.SoftwareDisplays, "software displays", _software.Dispose);
        context.Shutdown.Add(ShutdownStage.MonitorEngine, "engine", _engine.Dispose);
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(DisplaysPageViewModel.Info, () => new DisplaysPageViewModel(_service!))
        {
            Registers = () => _service = new DisplaysService(_engine, _context!.Actions, _context.SuggestedKeys, _context.Osd, _software, _context.Report,
                _context.Presets, _context.OpenPresetManager, _context.States, _context.Hardware, DisplaysPageViewModel.Info.Title),
        },
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[] { ViewTemplate.For<DisplaysPageViewModel, DisplaysPage>() };
}
