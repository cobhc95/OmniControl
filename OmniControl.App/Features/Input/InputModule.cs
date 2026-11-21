using Avalonia.Controls.Templates;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.Input.Pages.Gestures;
using OmniControl.App.Features.Input.Pages.Mouse;
using OmniControl.App.Features.Input.Rules;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input;

/// Mouse, trackpad and gestures: one system-wide hook shared by the button mappings, the gestures, the custom rules and the trackpad, and their page.
/// <param name="rememberedPrograms">The program paths the mixer remembers, offered by the rule editor as programs to choose.</param>
public sealed class InputModule(Func<IEnumerable<string>> rememberedPrograms) : IFeatureModule
{
    private readonly MouseHook _hook = new();
    private readonly PointerSettingsService _pointer = new(); // at start-up it puts back a button swap a crashed run left on trial
    private GestureService? _gestures;
    private MouseService? _mouse;
    private RuleService? _rules;
    private TrackpadService? _trackpad;
    private MarginOverlay? _margins;
    private PointerActions? _pointerActions;
    private InputActions? _actions;
    private FeatureContext? _context;

    public void Register(FeatureContext context)
    {
        _context = context;
        // Gestures are asked first, so a scroll or middle click they take never reaches the button mappings: the wheel gestures have
        // their own hook handler ahead of the mouse service's, and the mouse service asks for a middle-click gesture before its mapping.
        var gestures = _gestures = new GestureService(_hook, context.Runner.RunLevel, context.Report, context.AppScopeGate);
        var mouse = _mouse = new MouseService(_hook, context.Runner.Run, context.Runner.RunHeld, context.Report, gestures.MiddleClickAt, context.AppScopeGate, context.States,
            id => context.Actions.Find(id)?.KeyEquivalent);
        context.Actions.Moved += mouse.FollowMoved;
        context.Actions.Moved += gestures.FollowMoved;
        // Custom rules are asked before both (their handler goes first on the hook), so a rule that matches wins.
        var rules = _rules = new RuleService(_hook, gestures, mouse, context.Actions, context.Runner, rememberedPrograms, context.Report, context.AskAsync);
        // Trackpad gestures are handed to the same hook, so rules and mappings treat them like button presses. The touchpad watcher runs
        // while a mapping, a rule or the Mouse page acts on a gesture; the low-level hook while a handler's owner needs it (its own answer).
        var trackpad = _trackpad = new TrackpadService(_hook, mouse, context.Hardware, () => mouse.NeedsTrackpad || rules.NeedsTrackpad);
        // Last handler: Back and Forward over OmniControl or the desktop that nothing took are swallowed. It needs the hook at all times.
        _ = new BackForwardGuard(_hook);
        var margins = _margins = new MarginOverlay(gestures.Settings);
        var pointerActions = _pointerActions = new PointerActions(_pointer, context.Osd, context.AskAsync, context.Report);
        var actions = _actions = new InputActions(mouse, gestures, margins, pointerActions, context.Osd);
        StartHook(context.Report);
        void Recheck()
        {
            _hook.Recheck();
            trackpad.SyncWatcher();
        }
        mouse.Applied += Recheck;
        mouse.NeedsChanged += Recheck;
        rules.Loaded += Recheck; // also after every gesture change: the rules follow the "Enable gestures" switch

        context.Imports.OnReload(DataStores.GestureRules, rules.ReloadFromDisk);
        context.Imports.OnReload(DataStores.Gestures, gestures.ReloadFromDisk);
        context.Imports.OnReload(DataStores.Mouse, mouse.ReloadFromDisk);
        context.Shutdown.Add(ShutdownStage.Pointer, "input actions", () =>
        {
            actions.Dispose();
            pointerActions.Dispose();
        });
        context.Shutdown.Add(ShutdownStage.Pointer, "margins", margins.Dispose);
        context.Shutdown.Add(ShutdownStage.Pointer, "trackpad", trackpad.Dispose);
        context.Shutdown.Add(ShutdownStage.Pointer, "pointer", _pointer.Dispose); // a button swap that was never kept goes back
        context.Shutdown.Add(ShutdownStage.Pointer, "input needs", () =>
        {
            mouse.Applied -= Recheck;
            mouse.NeedsChanged -= Recheck;
            rules.Loaded -= Recheck;
        });
        context.Shutdown.Add(ShutdownStage.MouseHook, "rules", rules.Dispose);
        context.Shutdown.Add(ShutdownStage.MouseHook, "mouse", () =>
        {
            context.Actions.Moved -= mouse.FollowMoved;
            context.Actions.Moved -= gestures.FollowMoved;
            mouse.Dispose();
        });
        context.Shutdown.Add(ShutdownStage.MouseHook, "mouse hook", _hook.Dispose);
    }

    private void StartHook(Action<string> report)
    {
        _hook.Log = line => AppDataStore.AppendLog(LogFiles.Mouse, line);
        _hook.Problem = line => AppDataStore.AppendLog(Guard.LogFile, $"Mouse hook: {line}");
        var started = true;
        if (Guard.Run("Start-up: mouse hook", () => started = _hook.Start()) is not null) started = false;
        if (!started) report("Couldn't start mouse remapping and gestures: Windows refused the mouse hook.");
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(MousePageViewModel.Info, () => new MousePageViewModel(_mouse!, _trackpad!, _pointer, _pointerActions!, _context!.Actions, _context.Report,
            _context.OpenPresetManager, _context.AppScopes, new GestureGroups(_gestures!, _rules!, _margins!, _context.Actions, _context.AskAsync, _context.AppScopes)))
        {
            Registers = () => _actions!.Register(_context!.Actions, _context.Presets),
        },
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<MousePageViewModel, MousePage>(),
        ViewTemplate.For<MouseButtonItem, MouseButtonRow>(),
        ViewTemplate.For<GestureItem, GestureRow>(),
        ViewTemplate.For<RuleItem, RuleRow>(),
    };
}
