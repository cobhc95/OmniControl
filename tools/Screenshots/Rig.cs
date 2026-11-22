using OmniControl.App.Features;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Icons;
using OmniControl.App.Features.Osd;
using OmniControl.App.Shared.Services.Backup;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Presets;
using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.Screenshots;

/// The shell's shared pieces, made the way <c>ShellViewModel</c> makes them, minus everything that reaches Windows: the hardware
/// watches are never started (no listener window), the master list's hotkey thread is ended before any key is given to it (so no
/// RegisterHotKey ever runs), and the run path handed to features is <see cref="NoRunner"/>.
internal sealed class Rig
{
    public Rig()
    {
        Actions = new HotkeyRegistry(Hardware, Report, new OmniActionSettings(), () => States, Gate);
        Actions.Dispose(); // ends its hotkey thread now: the keys the samples show are never registered with Windows
        States = new StateRegistry(chainNow: () => null);
        Icons = new IconStore(Actions);
        IconPicker = new IconPicker(Icons);
        Lists = new AppListStore(AppListSettings.Load());
        Scopes = new AppScopes(Lists, () => { });
        ScopeGate = new AppScopeGate(Lists);
        if (Osd.Settings.HideWindowsOsd) throw new InvalidOperationException("The sample OSD must never hide Windows' own pop-up.");
        var settings = AppSettings.Current;
        Features = new FeatureSwitches(settings.Slot(s => s.FeaturesOff, (s, off) => s.FeaturesOff = off), settings.Slot(s => s.FeaturesFromSetup, (s, chosen) => s.FeaturesFromSetup = chosen));
        Context = new FeatureContext(Actions, Runner, States, Hardware, Osd, Presets, OpenPresets, Ask, AskText, Report, (_, _) => { }, Scopes, ScopeGate,
            IconPicker, Gate, Power, setId => new SuggestedKeysSwitch(Actions, setId), settings, new ImportHooks(), new ShutdownSteps(), Features, Rules);
    }

    public FeatureSwitches Features { get; }

    /// The automation rules' slot features plug their own rules into; empty here (no automation), so a feature's rule reads as none.
    public FeatureRulesSlot Rules { get; } = new();

    /// What every feature module is given, made of the pieces above (as the shell makes it).
    public FeatureContext Context { get; }

    public HardwareWatches Hardware { get; } = new();
    public HotkeyRegistry Actions { get; }
    public StateRegistry States { get; }
    public ActionGateSlot Gate { get; } = new();
    public PresetGroups Presets { get; } = new();
    public IconStore Icons { get; }
    public IconPicker IconPicker { get; }
    public AppListStore Lists { get; }
    public AppScopes Scopes { get; }
    public AppScopeGate ScopeGate { get; }
    public NoRunner Runner { get; } = new();
    public NoPower Power { get; } = new();

    /// The OSD, as every feature gets it. Its listeners only hear Windows (volume, microphone, media, the brightness event); with the
    /// temp data folder it has no hidden Windows pop-up to put back, and its pop-ups would open only in the headless platform.
    public OsdService Osd { get; } = new();

    /// The sidebar pages made so far, in sidebar order (the pickers' sections are made from them).
    public List<PageViewModel> Pages { get; } = new();

    /// The pages' Restore defaults, as their modules register them; App settings' "Restore all" covers these.
    public List<IRestorable> Restorables { get; } = new();

    /// Gives a page its Restore defaults button, as the shell does when the page is made (the question is always declined).
    public void Restore(PageViewModel page, IRestorable part)
    {
        Restorables.Add(part);
        page.AttachRestorer(new PageRestore(new RestoreRunner(part.Key, page.Title, new[] { part }, false, _ => Task.FromResult(false), new FileSnapshotStore())));
    }

    public static Task<bool> Ask(DialogMessage message) => Task.FromResult(false);

    public static Task<string?> AskText(DialogMessage message) => Task.FromResult<string?>(null);

    public static void Report(string line) => Console.WriteLine($"      (report) {line}");

    public static void OpenPresets(string? section, string? group) { }
}

/// The run path features get: it never runs anything.
internal sealed class NoRunner : IActionRunner
{
    public bool Run(string id) => false;
    public bool Run(string id, RunSettings? settings) => false;
    public Action? RunHeld(string id, RunSettings settings) => null;
    public bool RunLevel(string levelId, bool up, int percent) => false;
    public RunChain? Chain => null;
    public void RunWithin(RunChain chain) { }
    public event Action<HotkeyAction, RunChain?>? Ran { add { } remove { } }
}

/// Power events that never come.
internal sealed class NoPower : IPowerEvents
{
    public event Action? Woke { add { } remove { } }
    public event Action<PowerEventKind>? Leaving { add { } remove { } }
    public event Action<PowerCountdown?>? Countdown { add { } remove { } }
    public void Counting(PowerCountdown? now) { }
    public bool TakeLogin(bool startedByWindows) => false;
}
