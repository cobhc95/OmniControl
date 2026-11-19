using System.ComponentModel;
using System.Text.Json;

namespace OmniControl.App.Shared.Ui.Restore;

/// What a restore or an undo did: whether it ran, the one calm line to show and whether "Undo" is on offer.
public sealed record RestoreResult(bool Ran, string Line, bool CanUndo);

/// "Restore defaults" for one page or for every page (pure logic, no window): says why it cannot run, asks once, takes a copy of every
/// page's settings BEFORE changing any, restores each page in order (one that fails is named and the others still go on), and undoes
/// with the copy. The copy is kept in memory and in the file of <see cref="ISnapshotStore"/>.
public sealed class RestoreRunner
{
    private readonly string _key, _name;
    private readonly IReadOnlyList<IRestorable> _parts;
    private readonly bool _all;
    private readonly Func<RestoreQuestion, Task<bool>> _confirm;
    private readonly ISnapshotStore _store;
    private Dictionary<string, string>? _copies;
    private List<IRestorable> _ran = new();

    /// <param name="all">True for the combined restore of every page (one question listing them all).</param>
    public RestoreRunner(string key, string name, IReadOnlyList<IRestorable> parts, bool all, Func<RestoreQuestion, Task<bool>> confirm, ISnapshotStore store)
        => (_key, _name, _parts, _all, _confirm, _store) = (key, name, parts, all, confirm, store);

    /// Why the button is grey ("Already at the defaults", or a part's reason), or null when a restore can run.
    public string? Blocked
    {
        get
        {
            var reasons = _parts.Select(ReasonOf).ToList();
            return reasons.All(r => r is not null) ? reasons.FirstOrDefault(r => r != RestoreWords.AtDefaults) ?? RestoreWords.AtDefaults : null;
        }
    }

    private static string? ReasonOf(IRestorable part) => part.Unavailable ?? (part.IsAtDefaults ? RestoreWords.AtDefaults : null);

    /// A failure of Windows, the disk or a service that the app survives; anything else is a bug and is not hidden.
    private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or JsonException;

    public async Task<RestoreResult> RunAsync()
    {
        if (Blocked is { } why) return new(false, why + ".", false);
        var todo = _parts.Where(p => ReasonOf(p) is null).ToList();
        if (!await _confirm(_all ? RestoreWords.AskAll(todo) : RestoreWords.Ask(todo[0]))) return new(false, string.Empty, false);

        _copies = todo.ToDictionary(p => p.Key, p => p.CaptureSnapshot()); // every copy is taken before the first change
        _store.Save(_key, _copies);
        _ran = todo;
        var failed = new List<string>();
        foreach (var part in todo)
            if (!await Attempt(() => part.RestoreAsync())) failed.Add(part.Name);
        AppDataStore.FlushSaves(); // the defaults are on disk at once, not a moment later
        return new(true, failed.Count == 0 ? RestoreWords.Done(_name) : RestoreWords.Failed(failed), true);
    }

    /// Puts every restored page's earlier settings back, last first.
    public async Task<RestoreResult> UndoAsync()
    {
        var copies = _copies ?? _store.Load(_key);
        if (copies is null) return new(false, RestoreWords.NoCopy, false);
        var parts = _copies is null ? _parts.Where(p => copies.ContainsKey(p.Key)).ToList() : _ran;
        var failed = new List<string>();
        foreach (var part in Enumerable.Reverse(parts))
            if (copies.TryGetValue(part.Key, out var copy) && !await Attempt(() => part.UndoAsync(copy))) failed.Add(part.Name);
        _copies = null;
        AppDataStore.FlushSaves();
        return new(true, failed.Count == 0 ? RestoreWords.Undone : RestoreWords.UndoFailed(failed), false);
    }

    private static async Task<bool> Attempt(Func<Task<bool>> step)
    {
        try { return await step(); }
        catch (Exception ex) when (IsExpected(ex)) { return false; } // reported by name in the line; the other pages still go on
    }
}
