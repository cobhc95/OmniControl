using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// What the effects engine changed outside OmniControl and must undo: programs whose sound it silenced and programs it sent to a
/// virtual cable, each with the user's own values. Written to <c>engine-journal.json</c> before the change, removed after the undo.
/// If OmniControl ends without undoing (a crash, a plug-in taking the process down, being killed), the next start puts every
/// program back before anything else runs (made before the mixer, like the software display ramp in SoftwareDisplayService).
/// Windows remembers a program's output device itself, so a routed program that is not running at start-up stays in the
/// journal until it is seen again and moved back.
/// </summary>
public sealed class EngineJournal
{
    private static readonly DataStore Store = DataStores.EngineJournal;

    /// <summary>One program to put back: its key (lower-case .exe path), its own volume, mute and pan, and its own device.</summary>
    public sealed class Entry
    {
        public string Key { get; set; } = string.Empty;
        public bool Silenced { get; set; }
        public bool Routed { get; set; }
        public int Volume { get; set; }
        public int Pan { get; set; }
        public bool Muted { get; set; }
        /// <summary>The program's own output device; empty = the default device.</summary>
        public string DeviceId { get; set; } = string.Empty;
    }

    private readonly Dictionary<string, Entry> _entries;

    /// <summary>Entries left by an earlier run, still to be put back (entries of this run are undone by the engine itself).</summary>
    private readonly HashSet<string> _leftOver;

    private EngineJournal(Dictionary<string, Entry> entries)
    {
        _entries = entries;
        _leftOver = entries.Keys.ToHashSet();
    }

    /// <summary>Reads the journal left by the last run and puts back every program that is running now.</summary>
    public static EngineJournal Recover()
    {
        var saved = AppDataStore.Load<List<Entry>>(Store) ?? new List<Entry>();
        var journal = new EngineJournal(saved.Where(e => e.Key.Length > 0).GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.Last()));
        journal.RestoreRunning();
        return journal;
    }

    /// <summary>The engine is about to change this program: remember how to undo it first.</summary>
    /// <param name="ownDevice">The device the program goes back to (null = Windows' default).</param>
    public void Note(string key, AppAudioEntry user, string? ownDevice, bool silenced, bool routed)
    {
        var entry = _entries.GetValueOrDefault(key) ?? new Entry { Key = key };
        entry.Silenced |= silenced;
        entry.Routed |= routed;
        (entry.Volume, entry.Pan, entry.Muted, entry.DeviceId) = (user.Volume, user.Pan, user.Muted, ownDevice ?? string.Empty);
        _entries[key] = entry;
        _leftOver.Remove(key);
        Save();
    }

    /// <summary>The engine undid its change itself.</summary>
    public void Forget(string key)
    {
        if (_entries.Remove(key)) Save();
    }

    /// <summary>Programs from an earlier run that still wait to be put back.</summary>
    public bool HasLeftOver => _leftOver.Count > 0;

    /// <summary>Puts back every program left over from an earlier run that has sound sessions now; the others wait for a later call.</summary>
    public void RestoreRunning()
    {
        if (_leftOver.Count == 0) return;
        var sessions = CoreAudio.Sessions();
        var done = new List<string>();
        foreach (var group in sessions.GroupBy(s => CoreAudio.ProcessPath(s.ProcessId) is { } path ? AppAudioEntry.KeyFor(path) : string.Empty))
        {
            if (!_leftOver.Contains(group.Key) || !_entries.TryGetValue(group.Key, out var entry)) continue;
            foreach (var s in group)
            {
                if (entry.Silenced)
                {
                    s.SetVolume(entry.Volume);
                    s.SetMuted(entry.Muted);
                    s.SetPan(entry.Pan);
                }
            }
            var routedBack = !entry.Routed
                || CoreAudio.TrySetProgramDevice(group.Select(s => s.ProcessId), entry.DeviceId.Length > 0 ? entry.DeviceId : null, out _);
            if (routedBack) done.Add(group.Key); // a refused move stays journalled and is tried again on the next call
        }
        foreach (var key in done)
        {
            _entries.Remove(key);
            _leftOver.Remove(key);
        }
        if (done.Count > 0) Save();
    }

    private void Save()
    {
        if (_entries.Count == 0) AppDataStore.Delete(Store);
        else AppDataStore.Save(Store, _entries.Values.ToList());
    }
}
