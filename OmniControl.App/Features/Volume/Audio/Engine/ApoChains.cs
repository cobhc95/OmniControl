using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// One row whose chain should run in Equalizer APO (the master, a microphone, a program on a cable with Equalizer APO on it): its key,
/// the name for the section's comment, its chain as parts (<see cref="FxFolders.PartsFor"/>: a device folder keeps its Mix), the device it runs on (null when there is none), why the APO can't run there (null =
/// it can), what the row then does ("Playing without effects.") and the fact its status names then.
internal sealed record ApoRow(string Key, string Title, IReadOnlyList<FxPart> Parts, ApoEndpoint? Endpoint, string? Missing, string Without,
    StatusKind MissingKind = StatusKind.NeedsApo);

/// What is on disk in one config folder: OmniControl's part is as it should be, and config.txt runs effects of its own (outside OmniControl's lines).
public sealed record ApoFolderFacts(bool InPlace, bool OwnEffects);

/// Where OmniControl's Equalizer APO text goes: the real config folders (<see cref="ApoConfigFiles"/>), or a fake in the tests.
internal interface IApoConfigStore
{
    /// Puts OmniControl's file and include line in <paramref name="folder"/>; the error sentence, or null when done.
    string? Apply(string folder, string text);

    /// Takes OmniControl's file and include line out of <paramref name="folder"/>; the error sentence, or null when done.
    string? Remove(string folder);

    /// What is really on disk (<paramref name="wanted"/> null = none of OmniControl's part); null while a file is busy.
    ApoFolderFacts? Read(string folder, string? wanted);

    /// Makes the APO of <paramref name="folder"/> load it again without changing it; the error sentence, or null.
    string? Touch(string folder);
}

/// The real config folders.
internal sealed class ApoConfigFolders : IApoConfigStore
{
    public static readonly ApoConfigFolders Instance = new();

    public string? Apply(string folder, string text) => ApoConfigFiles.Apply(folder, text);
    public string? Remove(string folder) => ApoConfigFiles.Remove(folder);
    public ApoFolderFacts? Read(string folder, string? wanted) => ApoConfigFiles.Read(folder, wanted);
    public string? Touch(string folder) => ApoConfigFiles.Touch(folder);
}

/// What OmniControl's part of every config folder should be: the chain text per folder (<see cref="Files"/>) and the folders that must hold
/// none of it (<see cref="Clean"/>). Immutable, so the folder watch reads it off the UI thread.
internal sealed record ApoTarget(IReadOnlyDictionary<string, string> Files, IReadOnlyList<string> Clean)
{
    public static readonly ApoTarget None = new(new Dictionary<string, string>(), Array.Empty<string>());

    public IEnumerable<string> Folders => Files.Keys.Concat(Clean);

    /// The chain text that belongs in <paramref name="folder"/>; null = none of OmniControl's part.
    public string? WantedIn(string folder) => Files.GetValueOrDefault(folder);

    public bool SameAs(ApoTarget other) =>
        Files.Count == other.Files.Count && Files.All(kv => other.Files.TryGetValue(kv.Key, out var text) && text == kv.Value)
        && Clean.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.Clean);
}

/// The chains that run in the user's Equalizer APO: puts each row's section into OmniControl.txt of the config folder whose
/// APO runs on that row's device (<see cref="ApoChainWriter"/>, <see cref="IApoConfigStore"/>), takes OmniControl's line and file out of
/// every other folder, writes only when the wanted text changes (what is really on disk is checked by <see cref="ApoKeeper"/>, which
/// puts back what another program took out), and says per row what is true: the chain was sent (not that Windows applied it), the APO is
/// missing there and why, the config no longer includes it (<see cref="SetDisplaced"/>), or a write failed. A failed write is tried
/// again no sooner than <see cref="Timing.ApoRetryGap"/>, so a locked folder is not rewritten on every change. The first call after
/// start-up always writes (or cleans), so a crashed run's lines are taken out. UI thread.
internal sealed class ApoChains
{
    private readonly Action<string> _report;
    private readonly IApoConfigStore _store;
    private readonly IrLevels _levels;

    /// What should be in place (updated on every change, whether or not the write worked).
    private ApoTarget _target = ApoTarget.None;
    /// The last write's errors by folder (kept until the next write, so a row keeps saying it failed).
    private Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);
    /// Folders whose config no longer includes OmniControl's part and that are left so (Keep in place is off).
    private readonly HashSet<string> _displaced = new(StringComparer.OrdinalIgnoreCase);
    private bool _synced;
    private DateTime _retryAt = DateTime.MinValue;
    private string? _reported;

    /// Folders whose chain file was just written (the keeper nudges Equalizer APO once the burst is over). UI thread.
    public event Action<IReadOnlyCollection<string>>? Wrote;

    /// <param name="report">The footer line: a failed clean-up of a folder no row writes to is said there once (nothing else shows it).</param>
    /// <param name="levelMeasured">An IR file's Auto level became known (worker thread): the owner applies the chains again.</param>
    /// <param name="store">Where the text goes (the real folders unless a test gives a fake).</param>
    public ApoChains(Action<string> report, Action levelMeasured, IApoConfigStore? store = null)
    {
        _report = report;
        _levels = new IrLevels(levelMeasured);
        _store = store ?? ApoConfigFolders.Instance;
    }

    /// What should be in place now.
    public ApoTarget Target => _target;

    /// The last write left a folder unwritten.
    public bool Failing => !_synced;

    /// Some folder's config no longer includes OmniControl's effects and is left so.
    public bool AnyDisplaced => _displaced.Count > 0;
    /// The rows a plan sends to Equalizer APO (<see cref="RoutePlan.ToApo"/>), each on its own device: the master on the real speakers
    /// (<see cref="RoutePlan.MasterApoDevice"/>); a program on the cable its run uses (<see cref="RunKind.ApoCable"/>). Pure: the chains and names come from the callers.
    /// <param name="chainOf">A row's chain (null key = the master).</param>
    /// <param name="nameOf">A row's name for the section's comment.</param>
    public static IReadOnlyList<ApoRow> RowsFor(RoutePlan plan, ApoState apo, Func<string?, IReadOnlyList<FxSlot>> chainOf, Func<string, string> nameOf)
    {
        const string Silent = "Playing without effects.";
        var rows = new List<ApoRow>();
        foreach (var key in plan.ToApo)
        {
            if (key == MixerService.MasterKey)
            {
                var speakers = plan.MasterApoDevice is { } id ? apo.PlaybackOf(id) : null;
                rows.Add(new ApoRow(key, nameOf(key), FxFolders.PartsFor(chainOf(null), plan.MasterApoDevice), speakers, EffectsRules.ApoMissingOn(apo, speakers), Silent, StatusKind.ApoNotApplied));
            }
            else if (plan.Runs.GetValueOrDefault(key)?.Cable is { } cable)
            {
                var onCable = apo.PlaybackOf(cable);
                rows.Add(new ApoRow(key, $"Program: {nameOf(key)}", new[] { new FxPart(null, chainOf(key)) }, onCable, EffectsRules.ApoMissingOn(apo, onCable), Silent));
            }
        }
        return rows;
    }

    /// Brings the config folders in line with <paramref name="rows"/>; returns each row's status.
    public IReadOnlyDictionary<string, EngineStatus> Apply(IReadOnlyList<ApoRow> rows, ApoState apo)
    {
        var statuses = new Dictionary<string, EngineStatus>();
        var sections = new Dictionary<string, List<ApoSection>>(StringComparer.OrdinalIgnoreCase);
        var placed = new List<(string Key, string Folder, ApoSection Section)>();
        foreach (var row in rows)
        {
            var folder = row.Endpoint is { } endpoint ? apo.FolderFor(endpoint) : null;
            if (row.Missing is not null || folder is null || row.Endpoint is null)
            {
                statuses[row.Key] = new EngineStatus(EngineState.Unavailable, $"{row.Missing ?? "Equalizer APO is not on this device"}. {row.Without}", null, row.MissingKind, RunMethod.Apo);
                continue;
            }
            var section = ApoChainWriter.Section(row.Title, row.Parts, ApoIds.DeviceKeyOf(row.Endpoint.EndpointId), row.Endpoint.Channels, apo.Blends, _levels);
            if (!sections.TryGetValue(folder, out var list)) sections[folder] = list = new List<ApoSection>();
            list.Add(section);
            placed.Add((row.Key, folder, section));
        }

        var target = TargetFor(sections, apo);
        if (!target.SameAs(_target) || (!_synced && DateTime.UtcNow >= _retryAt))
        {
            _target = target;
            _displaced.IntersectWith(target.Files.Keys); // a folder that should now be clean is cleaned, whatever the switch says
            Write();
        }

        foreach (var (key, folder, section) in placed)
            statuses[key] = StatusOf(folder, section, sections[folder].Any(s => s.Waiting));
        return statuses;
    }

    /// The wanted chain text per folder and the folders to clean. A folder whose IR level is still being measured keeps the text it
    /// had (or, with none yet, is neither written nor cleaned) until the measurement is in.
    private ApoTarget TargetFor(Dictionary<string, List<ApoSection>> sections, ApoState apo)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, list) in sections)
        {
            if (!list.Any(s => s.Waiting)) files[folder] = ApoChainWriter.File(list);
            else if (_target.WantedIn(folder) is { } before) files[folder] = before;
            else held.Add(folder);
        }
        return new ApoTarget(files, apo.ConfigFolders.Where(f => !files.ContainsKey(f) && !held.Contains(f)).ToList());
    }

    private EngineStatus StatusOf(string folder, ApoSection section, bool waiting)
    {
        if (_errors.TryGetValue(folder, out var error)) return new EngineStatus(EngineState.Failed, error, null, StatusKind.ApoFailed, RunMethod.Apo);
        if (waiting) return new EngineStatus(EngineState.Waiting, EffectsRules.ApoMeasuringIr, null, StatusKind.Starting, RunMethod.Apo);
        if (_displaced.Contains(folder)) return new EngineStatus(EngineState.Unavailable, $"{EffectsRules.ApoDisplaced}. Playing without effects.", null, StatusKind.ApoDisplaced, RunMethod.Apo);
        return new EngineStatus(EngineState.Running, $"{EffectsRules.ApoSent} · {PluginNote(section)}{EngineStatus.SkippedNote(section.Skipped)}", null, StatusKind.Running, RunMethod.Apo);
    }

    /// The delay, and how a VST2 plug-in runs there: Equalizer APO adds no delay of its own, but a plug-in's delay is not known to
    /// OmniControl; the plug-in runs inside Windows with the settings saved in OmniControl, its defaults while none are saved.
    private static string PluginNote(ApoSection section)
    {
        if (section.Plugins.Count == 0) return "no extra delay";
        var note = "plug-ins run inside Windows with the settings saved here, their delay not known";
        return section.Defaults.Count == 0 ? note : $"{note} · default settings until its window is opened: {string.Join(", ", section.Defaults)}";
    }

    /// Takes OmniControl's line and file out of every config folder (exit: effects run only while OmniControl runs).
    public void Clear(ApoState apo)
    {
        _target = new ApoTarget(new Dictionary<string, string>(), apo.ConfigFolders.ToList());
        _displaced.Clear();
        Write();
    }

    /// Puts OmniControl's whole part back where it should be (the keeper found it taken out, or the user asked with "Put back"): every
    /// folder is written again (unchanged files are left alone) and none is left displaced.
    public void PutBack()
    {
        _displaced.Clear();
        Write();
    }

    /// A failed write is tried again once its gap is over (the watchdog asks; a change in the chain writes at once anyway).
    public void RetryIfDue()
    {
        if (!_synced && DateTime.UtcNow >= _retryAt) Write();
    }

    /// Marks <paramref name="folder"/> as no longer including OmniControl's part (left so while Keep in place is off), or as including it
    /// again; true when that changed. Only a folder that should hold a chain can be displaced.
    public bool SetDisplaced(string folder, bool displaced) =>
        displaced ? _target.Files.ContainsKey(folder) && _displaced.Add(folder) : _displaced.Remove(folder);

    /// Makes Equalizer APO load <paramref name="folders"/> again (after a burst of writes); a failure is said once.
    public void Nudge(IEnumerable<string> folders)
    {
        foreach (var folder in folders.Where(f => _target.Files.ContainsKey(f)))
            if (_store.Touch(folder) is { } error) Say(error);
    }

    /// Writes <see cref="_target"/>: removes OmniControl's part from every folder to clean, then writes each wanted file (except displaced ones).
    private void Write()
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _target.Clean)
            if (_store.Remove(folder) is { } error) errors[folder] = error;
        var written = new List<string>();
        foreach (var (folder, text) in _target.Files.Where(f => !_displaced.Contains(f.Key)))
        {
            if (_store.Apply(folder, text) is { } error) errors[folder] = error;
            else written.Add(folder);
        }
        _errors = errors;
        _synced = errors.Count == 0;
        if (!_synced) _retryAt = DateTime.UtcNow + Timing.ApoRetryGap;
        var cleanUp = errors.Where(e => !_target.Files.ContainsKey(e.Key)).Select(e => e.Value).FirstOrDefault();
        if (cleanUp is not null) Say(cleanUp);
        else _reported = null; // a later failure is said again
        if (written.Count > 0) Wrote?.Invoke(written);
    }

    private void Say(string sentence)
    {
        if (sentence == _reported) return;
        _reported = sentence;
        _report(sentence);
    }
}
