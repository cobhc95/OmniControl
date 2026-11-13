using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// One row whose chain should run in Equalizer APO (Fast mode: the master, a microphone): its key, the name for the section's comment,
/// its chain, the device it runs on (null when there is none), why the APO can't run there (null = it can) and what the row then does
/// ("Playing without effects.").
/// </summary>
internal sealed record ApoRow(string Key, string Title, IReadOnlyList<FxSlot> Chain, ApoEndpoint? Endpoint, string? Missing, string Without);

/// <summary>
/// Fast mode's chains in Equalizer APO (or OmniControl APO): puts each row's section into OmniControl.txt of the config folder whose
/// APO runs on that row's device (<see cref="ApoChainWriter"/>, <see cref="ApoConfigFiles"/>), takes OmniControl's line and file out of
/// every other folder, touches files only when the wanted text changes, and says per row what is true: the chain was sent (not that
/// Windows applied it), the APO is missing there and why, or a write failed. The first call after start-up always writes (or cleans),
/// so a crashed run's lines are taken out. UI thread.
/// </summary>
internal sealed class ApoChains
{
    private static readonly IReadOnlyDictionary<string, string> Nothing = new Dictionary<string, string>();

    private readonly Action<string> _report;

    /// <summary>What was last put in place: config folder -> OmniControl.txt text (empty = OmniControl's line taken out everywhere).</summary>
    private IReadOnlyDictionary<string, string> _applied = Nothing;
    private bool _synced;
    private string? _reported;

    /// <param name="report">The footer line: a failed clean-up of a folder no row writes to is said there once (nothing else shows it).</param>
    public ApoChains(Action<string> report) => _report = report;

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
                statuses[row.Key] = new EngineStatus(EngineState.Unavailable, $"{row.Missing ?? "Equalizer APO is not on this device"}. {row.Without}");
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
                ? new EngineStatus(EngineState.Failed, error)
                : new EngineStatus(EngineState.Running, $"{SoundModes.ApoSent(apo, folder)} · {PluginNote(section)}{EngineStatus.SkippedNote(section.Skipped)}");
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
    public static void Clear(ApoState apo) => PutInPlace(apo, Nothing);

    /// <summary>Removes OmniControl's line from every folder not wanted, then writes each wanted file; the errors by folder.</summary>
    private static Dictionary<string, string> PutInPlace(ApoState apo, IReadOnlyDictionary<string, string> wanted)
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in apo.ConfigFolders.Where(f => !wanted.ContainsKey(f)))
            if (ApoConfigFiles.Remove(folder) is { } error) errors[folder] = error;
        foreach (var (folder, text) in wanted)
            if (ApoConfigFiles.Apply(folder, text) is { } error) errors[folder] = error;
        return errors;
    }

    private static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var text) && text == kv.Value);
}
