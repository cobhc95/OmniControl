using Avalonia.Controls.Templates;
using OmniControl.App.Features.OtherPcs.Pages;
using OmniControl.App.Features.OtherPcs.SettingsImport;

namespace OmniControl.App.Features.OtherPcs;

/// Other PCs: the paired PCs' link (it listens only once a PC is paired or "Pair a PC" is opened), the import of settings from one, and the page.
/// <param name="shareDevices">The smart device set-ups this PC hands to a paired PC that asks.</param>
public sealed class OtherPcsModule(Func<bool, string> shareDevices) : IFeatureModule
{
    private OtherPcsService? _service;
    private FeatureContext? _context;

    public IPairedPcs Paired => _service!;

    /// Imports settings from a paired PC (App settings' backup offers it too).
    public ImportFromPcService ImportFrom { get; private set; } = null!;

    public void Register(FeatureContext context)
    {
        _context = context;
        var service = _service = new OtherPcsService(context.Actions, context.Runner, context.States, new WindowsCredentialStore(), context.Report, shareDevices,
            context.AskAsync, new SettingsShare());
        context.Power.Leaving += service.Wake.AnnounceLeaving; // paired PCs are told before this one sleeps or shuts down
        ImportFrom = new ImportFromPcService(service, context.Imports, context.Report);
        context.Shutdown.Add(ShutdownStage.OtherPcs, "other PCs", () =>
        {
            context.Power.Leaving -= service.Wake.AnnounceLeaving;
            service.Dispose(); // its link ends and what other PCs shared leaves the master lists
        });
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(OtherPcsService.Category, () => new OtherPcsPageViewModel(_service!, _context!.Actions, _context.AskAsync, _context.AskTextAsync, _context.Report, ImportFrom)),
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<OtherPcsPageViewModel, OtherPcsPage>(),
        ViewTemplate.For<PcItem, PcDetail>(),
        ViewTemplate.For<SetupTabItem, PairPcPanel>(),
    };
}
