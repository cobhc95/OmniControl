using OmniControl.App.Services;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.OtherPcs;

/// <summary>
/// What OmniControl turned on for waking this PC, recorded before each change (only settings that were off, so putting them back is exact), kept
/// in <see cref="DataStores.WakeUndo"/> so "Undo my changes" works after the window closes or the app restarts.
/// </summary>
public sealed class WakeUndoLog
{
    private sealed class Saved
    {
        public List<string> Changed { get; set; } = new();
    }

    /// <summary>The settings that were off and that OmniControl has turned on since the last undo.</summary>
    public IReadOnlyList<WakeFix> Changed => (AppDataStore.Load<Saved>(DataStores.WakeUndo)?.Changed ?? new())
        .Select(t => (Ok: WakeFixer.TryParse(t, out var fix), Fix: fix)).Where(x => x.Ok).Select(x => x.Fix).Distinct().ToList();

    public bool Any => Changed.Count > 0;

    /// <summary>Records the settings of <paramref name="fixes"/> that are off in <paramref name="before"/>; returns the ones newly added (to take back if the change never happened).</summary>
    public IReadOnlyList<WakeFix> Record(IEnumerable<WakeFix> fixes, WakeFacts before)
    {
        var have = Changed.ToList();
        var added = fixes.Distinct().Where(f => WakeFixer.Changes(f, before) && !have.Contains(f)).ToList();
        if (added.Count > 0) Write(have.Concat(added));
        return added;
    }

    public void Forget(IEnumerable<WakeFix> fixes) => Write(Changed.Except(fixes));

    public void Clear() => AppDataStore.Delete(DataStores.WakeUndo);

    private static void Write(IEnumerable<WakeFix> fixes)
    {
        var list = fixes.Select(f => f.ToString()).ToList();
        if (list.Count == 0) AppDataStore.Delete(DataStores.WakeUndo);
        else AppDataStore.Save(DataStores.WakeUndo, new Saved { Changed = list });
    }
}
