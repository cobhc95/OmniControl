using Avalonia.Controls.Templates;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.OtherPcs.Pages;
using OmniControl.App.Features.OtherPcs.SettingsImport;

namespace OmniControl.App.Features.OtherPcs;

/// Other PCs: the paired PCs' link (it listens only once a PC is paired or "Pair a PC" is opened), the import of settings from one, and the page.
/// <param name="shareDevices">The smart device set-ups this PC hands to a paired PC that asks.</param>
public sealed class OtherPcsModule(Func<bool, string> shareDevices) : IFeatureModule
{
    private OtherPcsService? _service;
    private FeatureContext? _context;
    private OtherPcsPageViewModel? _page;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.OtherPcs };

    /// The paired PCs; null while Other PCs is off.
    public IPairedPcs? Paired => _service;

    /// Imports settings from a paired PC (App settings' backup offers it too); null while Other PCs is off.
    public ImportFromPcService? ImportFrom { get; private set; }

    public void Register(FeatureContext context)
    {
        _context = context;
        var service = _service = new OtherPcsService(context.Actions, context.Runner, context.States, new WindowsCredentialStore(), context.Report, shareDevices,
            context.AskAsync, new SettingsShare());
        context.Power.Leaving += service.Wake.AnnounceLeaving; // paired PCs are told before this one sleeps or shuts down
        context.Power.Countdown += service.AnnounceCountdown; // and when a power countdown here starts, runs out or ends
        ImportFrom = new ImportFromPcService(service, context.Imports, context.Report);
        context.Shutdown.Add(ShutdownStage.OtherPcs, "other PCs", () =>
        {
            context.Power.Leaving -= service.Wake.AnnounceLeaving;
            context.Power.Countdown -= service.AnnounceCountdown;
            service.Dispose(); // its link ends and what other PCs shared leaves the master lists
        });
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(OtherPcsPageViewModel.Info, () => _page = new OtherPcsPageViewModel(_service!, _context!.AskAsync, _context.AskTextAsync, _context.Report, ImportFrom!))
        {
            Registers = RegisterPageActions,
        },
    };

    /// The page's tab actions: they act only while the page is on screen, so a page not made yet does nothing.
    private void RegisterPageActions()
    {
        var actions = _context!.Actions;
        actions.Register(new HotkeyAction("otherpcs.tab.new", "New PC tab", OtherPcsService.Category, "new pc tab pair another computer plus add", _ => _page?.RunTabAction(newTab: true))
            { SuggestedIcons = new[] { "plus" } });
        actions.Register(new HotkeyAction("otherpcs.tab.close", "Close PC tab", OtherPcsService.Category, "close pc tab unpair remove computer", _ => _page?.RunTabAction(newTab: false))
            { SuggestedIcons = new[] { "trash" } });
    }

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<OtherPcsPageViewModel, OtherPcsPage>(),
        ViewTemplate.For<PcItem, PcDetail>(),
        ViewTemplate.For<SetupTabItem, PairPcPanel>(),
    };
}
