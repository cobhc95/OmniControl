using Avalonia.Controls.Templates;
using OmniControl.App.Features.Displays.Pages;
using OmniControl.Core;

namespace OmniControl.App.Features.Displays;

/// Displays: the monitors' hardware engine, the software adjustments, the displays' service (their models, actions, states, presets and
/// watches) and the page. The software adjustments are made with the module, so a picture a crashed run left changed is put back even while
/// the feature is off; the monitors are first looked at when it starts.
public sealed class DisplaysModule : IFeatureModule
{
    private readonly SoftwareDisplayService _software = new();
    private MonitorEngine? _engine;
    private FeatureContext? _context;
    private DisplaysService? _service;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Displays };

    public void Register(FeatureContext context)
    {
        _context = context;
        var engine = _engine = new MonitorEngine();
        context.Imports.OnReload(DataStores.Displays, () => _service?.ReloadSettings() ?? false);
        context.Shutdown.Add(ShutdownStage.Pages, "displays", () => _service?.Dispose());
        context.Shutdown.Add(ShutdownStage.SoftwareDisplays, "software displays", _software.Dispose);
        context.Shutdown.Add(ShutdownStage.MonitorEngine, "engine", engine.Dispose);
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(DisplaysPageViewModel.Info, () => new DisplaysPageViewModel(_service!), () => DisplaysPageViewModel.Words(_service))
        {
            Registers = () => _service = new DisplaysService(_engine!, _context!.Actions, _context.SuggestedKeys, _context.Osd, _software, _context.Report,
                _context.Presets, _context.OpenPresetManager, _context.States, _context.Hardware, DisplaysPageViewModel.Info.Title),
            Restore = () => _service!.NewRestorable(DisplaysPageViewModel.Info.RestoreKey!),
        },
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[] { ViewTemplate.For<DisplaysPageViewModel, DisplaysPage>() };
}
