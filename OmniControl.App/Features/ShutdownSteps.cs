namespace OmniControl.App.Features;

/// The one shut-down order. Each step is added by its owner at its stage; steps of one stage run in the order they were added. The steps that
/// put Windows back (displays, pointer, mouse hook, effects) keep their places here, and each runs on its own (<see cref="Guard"/>), so one
/// that throws never skips the next.
public enum ShutdownStage
{
    Search,
    Restore,
    HardwareWatches, // before the services whose refreshes it calls
    PowerJournal,
    WinAKey, // gives Win+A back to Windows
    Tray,
    PresetManager,
    AppListManager,
    ActionsWindow,
    HotkeysSwitch,
    PresetShortcuts,
    PresetGroups,
    Pages,
    SoftwareDisplays, // puts every display's picture back
    Resources, // download shaping ends, the network limits and blocks leave Windows, the per-program traffic session stops, the taskbar gadget closes
    Pointer, // margins, trackpad, then a button swap that was never kept goes back
    Automation,
    MouseHook, // rules, mouse, then the hook itself
    OtherPcs, // its link ends and what other PCs shared leaves the master lists
    Icons,
    Hotkeys,
    OsdLinks,
    Osd,
    Effects, // plug-in editors close (and save) first; every program's own sound is put back, the APO gets an empty chain
    AppWindows, // the about window, the update check
    Mixer, // effects support, volume presets, the mixer
    MonitorEngine,
    Flush, // every waiting save (AppDataStore.SaveSoon) and log line is written, once everything above has saved
    // Last: a plug-in's own teardown can hang (a JUCE plug-in disconnecting every UI Automation provider of the process deadlocks with
    // Avalonia's), so it runs after everything is saved and put back, and the process ends regardless.
    Plugins,
}

/// The shut-down steps of every owner, run once in <see cref="ShutdownStage"/> order. A feature that can be switched off adds its steps through
/// its own view (<see cref="Owned"/>): they keep their places in the one order at exit, and run alone, in the same order, when it is switched off.
public sealed class ShutdownSteps
{
    private readonly List<(ShutdownStage Stage, string What, Action Step, ShutdownSteps Owner, bool OffOnly)> _steps;

    public ShutdownSteps() => _steps = new();

    private ShutdownSteps(List<(ShutdownStage, string, Action, ShutdownSteps, bool)> steps) => _steps = steps;

    public void Add(ShutdownStage stage, string what, Action step) => _steps.Add((stage, what, step, this, false));

    /// A step only for the feature being switched off while the app runs (giving back what the app keeps over a restart), never at exit.
    public void AddWhenOff(ShutdownStage stage, string what, Action step) => _steps.Add((stage, what, step, this, true));

    /// A view of the same order whose own steps <see cref="RunOwn"/> runs.
    public ShutdownSteps Owned() => new(_steps);

    /// Runs every step but the switch-off ones, each guarded; OrderBy is stable, so a stage keeps the order its steps were added in.
    public void Run() => RunWhere(s => !s.OffOnly);

    /// Runs and drops only the steps added through this view.
    public void RunOwn() => RunWhere(s => s.Owner == this);

    private void RunWhere(Func<(ShutdownStage Stage, string What, Action Step, ShutdownSteps Owner, bool OffOnly), bool> mine)
    {
        var steps = _steps.Where(mine).OrderBy(s => s.Stage).ToList();
        _steps.RemoveAll(steps.Contains);
        foreach (var (_, what, step, _, _) in steps) Guard.Run($"Shut-down: {what}", step);
    }
}
