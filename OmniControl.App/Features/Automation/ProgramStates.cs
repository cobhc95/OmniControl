using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Scripts;

namespace OmniControl.App.Features.Automation;

/// What a picker row of a program offers beyond its state: the .exe it is known by and, for a program the user added, taking it out of the list.
public sealed record ProgramFacts(string? Path, Action? Remove);

/// Programs as states: "&lt;Name&gt; is running", one on/off state per program in the master list (id "program.&lt;identity&gt;", section
/// <see cref="Section"/>). The owner is Automation: it registers a state for each program the user added, each a rule's trigger names and,
/// only while a trigger picker is open, each program its one read found (<see cref="Offer"/>), and drops the last kind when the picker closes.
/// The value is whether the program is in the list of running programs the rules' look reads (<see cref="Saw"/>), which it reads only while an
/// active rule follows a program (<see cref="Watching"/>), so nothing polls for a program nobody follows; otherwise it is the picker's one read.
/// A state of an active rule starts unknown and takes its first reading from the look, so a program already running never counts as starting.
public sealed class ProgramStates
{
    /// The section the program states are listed under, and the start of every program state's id.
    public const string Section = "Programs", Prefix = "program.";

    private const string AddedSource = "added by you", RuleSource = "in a rule", RunningSource = "running now", InstalledSource = "installed";
    private const string HowKnown = "Read from the programs running now, every few seconds while a rule follows it, otherwise when the list opened.";
    private const string AddTitle = "Choose a program";

    private readonly IStateRegistry _states;
    private readonly AutomationSettings _settings;
    private readonly Action<string> _log;
    private List<Known> _registered = new();
    private IReadOnlyList<string> _ruleIds = Array.Empty<string>();
    private IReadOnlyList<string> _activeIds = Array.Empty<string>();
    private IReadOnlySet<string>? _running;
    private DateTime _ranAt;
    private IReadOnlyList<ProgramFound> _found = Array.Empty<ProgramFound>();
    private int _pickers;

    private sealed record Known(string Id, string Name, string? Path, string Source);

    public ProgramStates(IStateRegistry states, AutomationSettings settings, Action<string> log) => (_states, _settings, _log) = (states, settings, log);

    /// The state id of a program given by name or path ("Chrome.exe" and "chrome" are the same program).
    public static string IdOf(string program) => Prefix + ProgramNames.Normalize(program);

    /// The program a state id names, or null for any other state.
    public static string? ProgramOf(string stateId) => stateId.StartsWith(Prefix, StringComparison.Ordinal) ? stateId[Prefix.Length..] : null;

    /// True while an active rule follows a program: the rules' look reads the running programs for it.
    public bool Watching => _activeIds.Count > 0;

    /// The program states the rules name, whether on or off.
    public IReadOnlyList<string> RuleStateIds => _ruleIds;

    public ProgramFacts? FactsOf(string stateId)
    {
        var known = _registered.FirstOrDefault(k => k.Id == stateId);
        if (known is null) return null;
        var added = _settings.AddedPrograms.FirstOrDefault(a => IdOf(a.Path) == stateId);
        return new ProgramFacts(known.Path, added is null ? null : () => Forget(added));
    }

    /// The rules changed: <paramref name="ruleStates"/> are the program states every rule's triggers name, <paramref name="activeStates"/> those of the rules
    /// that run now. A program followed for the first time is looked at from scratch (no reading until the look has one).
    public void Follow(IReadOnlyList<string> ruleStates, IReadOnlyList<string> activeStates)
    {
        var begins = _activeIds.Count == 0 && activeStates.Count > 0;
        _ruleIds = ruleStates;
        _activeIds = activeStates;
        if (begins) _running = null;
        Register();
        if (begins) _states.Notify(_activeIds);
    }

    /// The look read the running programs (UI thread): the states follow them.
    public void Saw(IReadOnlySet<string> running)
    {
        _running = running;
        _ranAt = DateTime.Now;
        _states.Notify(_activeIds);
    }

    /// A picker's one read finished: its programs are offered, and which of them run now is shown unless the look keeps that fresher.
    public void Offer(IReadOnlyList<ProgramFound> found, IReadOnlySet<string> running)
    {
        _found = found;
        if (!Watching)
        {
            _running = running;
            _ranAt = DateTime.Now;
        }
        if (!Register()) _states.Notify(_registered.Select(k => k.Id).ToList());
    }

    /// A trigger picker opened or closed; the programs only it offered are taken out of the master list with the last one.
    public void PickerOpened() => _pickers++;

    public void PickerClosed()
    {
        if (--_pickers > 0) return;
        _pickers = 0;
        _found = Array.Empty<ProgramFound>();
        Register();
    }

    /// The master list lets go of every program state (the service ends).
    public void Release()
    {
        _states.Unregister(_registered.Select(k => k.Id).ToList());
        _registered = new List<Known>();
    }

    /// The user chose an .exe to follow: it is kept with the name Windows gives it, listed at once and returned (null when cancelled or already listed).
    public async Task<StateEntry?> AddAsync(Visual from)
    {
        var path = await PathPicker.PickAsync(from, PathKind.Program, AddTitle, typeName: "Programs", patterns: new[] { "*.exe" });
        if (path is null) return null;
        var name = await Task.Run(() => ProgramFile.NameOf(path));
        var id = IdOf(path);
        _settings.AddedPrograms.RemoveAll(a => IdOf(a.Path) == id);
        _settings.AddedPrograms.Add(new AddedProgram(path, name));
        _settings.Save();
        _log($"program added to the trigger list: {name} ({path})");
        Register();
        return _states.Find(id);
    }

    private void Forget(AddedProgram added)
    {
        _settings.AddedPrograms.Remove(added);
        _settings.Save();
        Register();
    }

    /// The states the master list should hold now, in the order the picker shows them: the user's, the rules', the running, the installed.
    private List<Known> Wanted()
    {
        var wanted = new List<Known>();
        var seen = new HashSet<string>();
        void Add(string id, string name, string? path, string source)
        {
            if (seen.Add(id)) wanted.Add(new Known(id, name, path, source));
        }
        var found = _found;
        string NameOf(string id) => found.FirstOrDefault(p => Prefix + p.Id == id)?.Name ?? Capital(id[Prefix.Length..]);
        foreach (var added in _settings.AddedPrograms.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)) Add(IdOf(added.Path), added.Name, added.Path, AddedSource);
        foreach (var id in _ruleIds.OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase)) Add(id, NameOf(id), found.FirstOrDefault(p => Prefix + p.Id == id)?.Path, RuleSource);
        foreach (var program in found.Where(p => p.HasWindow).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)) Add(Prefix + program.Id, program.Name, program.Path, RunningSource);
        foreach (var program in found.Where(p => !p.HasWindow).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)) Add(Prefix + program.Id, program.Name, program.Path, InstalledSource);
        return wanted;
    }

    private static string Capital(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..];

    /// Puts the master list right; false when it already was (nothing was announced).
    private bool Register()
    {
        var wanted = Wanted();
        if (wanted.SequenceEqual(_registered)) return false;
        _states.Unregister(_registered.Select(k => k.Id).ToList());
        _registered = wanted;
        _states.Register(wanted.Select(Entry).ToList());
        return true;
    }

    private StateEntry Entry(Known known) =>
        new(known.Id, $"{known.Name} is running", Section, StateKind.OnOff, () => _running is { } running ? StateValue.Of(running.Contains(known.Id[Prefix.Length..]), _ranAt) : null,
            HowKnown, Keywords: $"program app process running open start close quit exit {known.Id[Prefix.Length..]} {known.Path}", Note: known.Source);
}
