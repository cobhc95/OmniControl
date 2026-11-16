using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// One row whose chain should run in Equalizer APO (the master, a microphone, a program on a cable with Equalizer APO on it): its key,
/// the name for the section's comment, its chain, the device it runs on (null when there is none), why the APO can't run there (null =
/// it can), what the row then does ("Playing without effects.") and the fact its status names then.
/// </summary>
internal sealed record ApoRow(string Key, string Title, IReadOnlyList<FxSlot> Chain, ApoEndpoint? Endpoint, string? Missing, string Without,
    StatusKind MissingKind = StatusKind.NeedsApo);

/// <summary>Where OmniControl's Equalizer APO text goes: the real config folders (<see cref="ApoConfigFiles"/>), or a fake in the tests.</summary>
internal interface IApoConfigStore
{
    /// <summary>Puts OmniControl's file and include line in <paramref name="folder"/>; the error sentence, or null when done.</summary>
    string? Apply(string folder, string text);

    /// <summary>Takes OmniControl's file and include line out of <paramref name="folder"/>; the error sentence, or null when done.</summary>
    string? Remove(string folder);
}

/// <summary>The real config folders.</summary>
internal sealed class ApoConfigFolders : IApoConfigStore
{
    public static readonly ApoConfigFolders Instance = new();

    public string? Apply(string folder, string text) => ApoConfigFiles.Apply(folder, text);
    public string? Remove(string folder) => ApoConfigFiles.Remove(folder);
}

/// <summary>
/// The chains that run in Equalizer APO (or OmniControl APO): puts each row's section into OmniControl.txt of the config folder whose
/// APO runs on that row's device (<see cref="ApoChainWriter"/>, <see cref="IApoConfigStore"/>), takes OmniControl's line and file out of
/// every other folder, touches files only when the wanted text changes, and says per row what is true: the chain was sent (not that
/// Windows applied it), the APO is missing there and why, or a write failed. The first call after start-up always writes (or cleans),
/// so a crashed run's lines are taken out. UI thread.
/// </summary>
internal sealed class ApoChains
{
    private static readonly IReadOnlyDictionary<string, string> Nothing = new Dictionary<string, string>();

    private readonly Action<string> _report;
    private readonly IApoConfigStore _store;

    /// <summary>What was last put in place: config folder -> OmniControl.txt text (empty = OmniControl's line taken out everywhere).</summary>
    private IReadOnlyDictionary<string, string> _applied = Nothing;
    private bool _synced;
    private string? _reported;

    /// <param name="report">The footer line: a failed clean-up of a folder no row writes to is said there once (nothing else shows it).</param>
    /// <param name="store">Where the text goes (the real folders unless a test gives a fake).</param>
    public ApoChains(Action<string> report, IApoConfigStore? store = null)
    {
        _report = report;
        _store = store ?? ApoConfigFolders.Instance;
    }

    /// <summary>
    /// The rows a plan sends to Equalizer APO (<see cref="RoutePlan.ToApo"/>), each on its own device: the master on the real speakers
    /// (<see cref="RoutePlan.MasterApoDevice"/>); a program on the cable its run uses (<see cref="RunKind.ApoCable"/>). Pure: the chains and names come from the callers.
    /// </summary>
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
                rows.Add(new ApoRow(key, nameOf(key), chainOf(null), speakers, EffectsRules.ApoMissingOn(apo, speakers), Silent, StatusKind.ApoNotApplied));
            }
            else if (plan.Runs.GetValueOrDefault(key)?.Cable is { } cable)
            {
                var onCable = apo.PlaybackOf(cable);
                rows.Add(new ApoRow(key, $"Program: {nameOf(key)}", chainOf(key), onCable, EffectsRules.ApoMissingOn(apo, onCable), Silent));
            }
        }
        return rows;
    }

    /// <summary>Brings the config folders in line with <paramref name="rows"/>; returns each row's status.</summary>
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
            var section = ApoChainWriter.Section(row.Title, row.Chain, ApoIds.DeviceKeyOf(row.Endpoint.EndpointId));
            if (!sections.TryGetValue(folder, out var list)) sections[folder] = list = new List<ApoSection>();
            list.Add(section);
            placed.Add((row.Key, folder, section));
        }

        var wanted = sections.ToDictionary(s => s.Key, s => ApoChainWriter.File(s.Value), StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!_synced || !Same(wanted, _applied))
        {
            errors = PutInPlace(apo, wanted);
            if (errors.Count == 0)
            {
                _applied = wanted;
                _synced = true;
            }
            var cleanUp = errors.Where(e => !wanted.ContainsKey(e.Key)).Select(e => e.Value).FirstOrDefault();
            if (cleanUp is not null && cleanUp != _reported) _report(cleanUp); // tried again on the next change
            _reported = cleanUp;
        }

        foreach (var (key, folder, section) in placed)
            statuses[key] = errors.TryGetValue(folder, out var error)
                ? new EngineStatus(EngineState.Failed, error, null, StatusKind.ApoFailed, RunMethod.Apo)
                : new EngineStatus(EngineState.Running, $"{EffectsRules.ApoSent(apo, folder)} · {PluginNote(section)}{EngineStatus.SkippedNote(section.Skipped)}", null, StatusKind.Running, RunMethod.Apo);
        return statuses;
    }

    /// <summary>
    /// The delay, and how a VST2 plug-in runs there: Equalizer APO adds no delay of its own, but a plug-in's delay is not known to
    /// OmniControl; the plug-in runs inside Windows with the settings saved in OmniControl, its defaults while none are saved.
    /// </summary>
    private static string PluginNote(ApoSection section)
    {
        if (section.Plugins.Count == 0) return "no extra delay";
        var note = "plug-ins run inside Windows with the settings saved here, their delay not known";
        return section.Defaults.Count == 0 ? note : $"{note} · default settings until its window is opened: {string.Join(", ", section.Defaults)}";
    }

    /// <summary>Takes OmniControl's line and file out of every config folder (exit: effects run only while OmniControl runs).</summary>
    public void Clear(ApoState apo) => PutInPlace(apo, Nothing);

    /// <summary>Removes OmniControl's line from every folder not wanted, then writes each wanted file; the errors by folder.</summary>
    private Dictionary<string, string> PutInPlace(ApoState apo, IReadOnlyDictionary<string, string> wanted)
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in apo.ConfigFolders.Where(f => !wanted.ContainsKey(f)))
            if (_store.Remove(folder) is { } error) errors[folder] = error;
        foreach (var (folder, text) in wanted)
            if (_store.Apply(folder, text) is { } error) errors[folder] = error;
        return errors;
    }

    private static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var text) && text == kv.Value);
}
