using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmniControl.App.Shared.Services.Backup;

/// How an import or a wipe changes the saved stores.
public enum RestoreMode
{
    /// Every backed-up store becomes the backup's (a store the backup lacks goes back to its defaults).
    Replace,

    /// A store that can merge gains what it lacks; one that can't is replaced by the backup's; a store the backup lacks is kept.
    Merge,

    /// Every saved store and undo copy is deleted: factory settings.
    Wipe,
}

/// An import or a wipe is only staged while OmniControl runs (its pages hold the settings and save them as they close), and carried out
/// at the next start, before anything is loaded (<see cref="ApplyPending"/>). The staged files sit in a folder of their own beside the
/// stores, with a plan saying how to apply them. Safety journals (<see cref="StoreKind.Safety"/>) are never touched, what a backup
/// may not set (<see cref="DataStore.NotImported"/>) keeps the value here, and effects' plug-in files and states are left out as an import
/// from another PC leaves them (<see cref="DataStore.NotImportedInside"/>). Works on any folder, so tests use a temporary one.
/// All or nothing: the whole replacement is worked out and checked first, written beside the stores with a copy of every file it changes,
/// then the plan becomes the journal of those changes (one atomic move) and only then are the stores changed. A start that finds the journal
/// (a crash or power cut mid-restore) finishes it from the checked copies, or rolls it back when they are damaged; a write that fails while
/// it runs rolls it back at once.
public static class PendingRestore
{
    private const string PendingFolder = "pending-restore";
    private const string PlanName = "plan.json";
    private const string NextFolder = "next", BeforeFolder = "before";

    /// One file the restore changes: its name in the data folder, the SHA-256 of its new bytes (null: deleted) and of its copy from before (null: it did not exist).
    private sealed record Change(string File, string? Next, string? Before);

    /// What is staged; <see cref="Commit"/> is set once the replacement is prepared and checked: the changes being made.
    private sealed record Plan(RestoreMode Mode, List<Change>? Commit = null);

    public static void Stage(string folder, BackupContents contents, RestoreMode mode)
    {
        var staging = Clear(folder);
        foreach (var (name, text) in contents.Files)
            if (DataStores.BackedUpByName(name) is { } store) // only the stores a backup may hold, by their own names
                AppDataStore.WriteAtomically(Path.Combine(staging, store.FileName), Encoding.UTF8.GetBytes(text), toDisk: true);
        WritePlan(staging, new Plan(mode));
    }

    public static void StageWipe(string folder) => WritePlan(Clear(folder), new Plan(RestoreMode.Wipe));

    /// Carries out a staged import or wipe, or finishes or rolls back one a crash interrupted, and removes it; gives a line for the log
    /// (null when nothing was staged). A damaged plan is dropped without changing anything.
    public static string? ApplyPending(string folder) => ApplyPending(folder, null);

    /// <paramref name="beforeStep"/> runs before each file the restore writes or deletes (tests fail or interrupt it there).
    internal static string? ApplyPending(string folder, Action? beforeStep) => new Run(folder, beforeStep ?? (() => { })).Go();

    private sealed class Run(string folder, Action beforeStep)
    {
        private readonly string _staging = Path.Combine(folder, PendingFolder);

        private string PlanPath => Path.Combine(_staging, PlanName);

        public string? Go()
        {
            if (!File.Exists(PlanPath)) return null;
            Plan? plan;
            try
            {
                plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(PlanPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"the staged restore could not be read, so it waits for the next start: {ex.Message}"; // nothing changed yet, or a journal to finish
            }
            catch (JsonException)
            {
                plan = null;
            }
            if (plan is null) return Drop("a staged restore had no plan; nothing was changed");
            var what = $"the staged {plan.Mode.ToString().ToLowerInvariant()}";
            return plan.Commit is { } commit ? Recover(what, commit) : Carry(plan.Mode, what);
        }

        private string Carry(RestoreMode mode, string what)
        {
            List<Change> commit;
            Dictionary<string, byte[]?> changes;
            try
            {
                changes = Prepare(mode);
                commit = KeepCopies(changes);
                beforeStep();
                WritePlan(_staging, new Plan(mode, commit)); // from here on, the next start finishes or rolls back what this one began
            }
            catch (InvalidDataException ex)
            {
                return Drop($"{what} was refused ({ex.Message}); nothing was changed");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Drop($"{what} could not be prepared ({ex.Message}); nothing was changed");
            }
            try
            {
                Put(changes.Select(c => (c.Key, c.Value)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return RollBack(commit, $"{what} failed ({ex.Message})");
            }
            return Drop($"applied {what}");
        }

        /// Every file the restore changes and its new bytes (null: deleted), worked out and checked before anything is written.
        private Dictionary<string, byte[]?> Prepare(RestoreMode mode)
        {
            var changes = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            if (mode != RestoreMode.Merge)
                foreach (var store in DataStores.All.Where(s => s.Kind != StoreKind.Safety))
                    foreach (var file in Directory.EnumerateFiles(folder, store.FileName))
                        changes[Path.GetFileName(file)] = null;
            if (mode == RestoreMode.Wipe)
            {
                if (File.Exists(Path.Combine(folder, BackupFile.BeforeImportName))) changes[BackupFile.BeforeImportName] = null;
                return changes;
            }
            var budget = BackupFile.MaxBytes;
            foreach (var store in DataStores.BackedUp)
            {
                var incomingPath = Path.Combine(_staging, store.FileName);
                if (!File.Exists(incomingPath)) continue;
                if ((budget -= new FileInfo(incomingPath).Length) < 0) throw new InvalidDataException("larger than a backup can be");
                if (!BackupFile.TryParse(File.ReadAllText(incomingPath), out var backup)) throw new InvalidDataException($"{store.FileName} is damaged");
                var target = Path.Combine(folder, store.FileName);
                var here = File.Exists(target) ? Parse(File.ReadAllText(target)) : null;
                if (store.NotImportedInside is { } inside) JsonMerge.StripInside(backup, inside); // a backup file never makes this PC load a library
                var incoming = JsonMerge.KeepOwn(backup, here, store.NotImported ?? Array.Empty<string>());
                var result = mode == RestoreMode.Merge && store.Mergeable && here is not null ? JsonMerge.Merge(here, incoming) : incoming;
                changes[store.FileName] = Encoding.UTF8.GetBytes(result!.ToJsonString());
            }
            return changes;
        }

        /// Writes each new file and a copy of each file it replaces into the staging folder, on the disk; gives the journal of the changes.
        private List<Change> KeepCopies(Dictionary<string, byte[]?> changes)
        {
            var next = Fresh(NextFolder);
            var before = Fresh(BeforeFolder);
            var commit = new List<Change>();
            foreach (var (file, bytes) in changes)
            {
                var target = Path.Combine(folder, file);
                var old = File.Exists(target) ? File.ReadAllBytes(target) : null;
                if (old is not null) Keep(Path.Combine(before, file), old);
                if (bytes is not null) Keep(Path.Combine(next, file), bytes);
                commit.Add(new Change(file, Hash(bytes), Hash(old)));
            }
            return commit;
        }

        private void Keep(string path, byte[] bytes)
        {
            beforeStep();
            AppDataStore.WriteAtomically(path, bytes, toDisk: true);
        }

        /// A crash or power cut stopped a restore part way: finish it from the checked new files, else put back the checked copies.
        private string Recover(string what, List<Change> commit)
        {
            if (commit.Any(c => !IsPlainName(c.File))) return Drop($"{what} named a file outside the data folder; it was dropped");
            try
            {
                if (Copies(commit, NextFolder, c => c.Next) is { } next)
                {
                    Put(next);
                    return Drop($"finished {what}, which was interrupted");
                }
                return RollBack(commit, $"{what} was interrupted and its new files are damaged");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return RollBack(commit, $"{what} was interrupted and could not be finished ({ex.Message})");
            }
        }

        private string RollBack(List<Change> commit, string why)
        {
            try
            {
                if (Copies(commit, BeforeFolder, c => c.Before) is not { } before)
                    return Drop($"{why}; the copies from before it are damaged, so it could not be rolled back");
                Put(before);
                return Drop($"{why}; it was rolled back, nothing was changed");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"{why}; it could not be rolled back fully, so the next start retries ({ex.Message})"; // journal and copies stay
            }
        }

        /// Each change's bytes from a staging subfolder (null for a file to delete), or null when one is missing or not what was kept.
        private List<(string File, byte[]? Bytes)>? Copies(List<Change> commit, string sub, Func<Change, string?> hashOf)
        {
            var copies = new List<(string, byte[]?)>();
            foreach (var change in commit)
            {
                if (hashOf(change) is not { } hash)
                {
                    copies.Add((change.File, null));
                    continue;
                }
                var path = Path.Combine(_staging, sub, change.File);
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                if (Hash(bytes) != hash) return null;
                copies.Add((change.File, bytes));
            }
            return copies;
        }

        private void Put(IEnumerable<(string File, byte[]? Bytes)> files)
        {
            foreach (var (file, bytes) in files)
            {
                beforeStep();
                var target = Path.Combine(folder, file);
                if (bytes is null) File.Delete(target);
                else AppDataStore.WriteAtomically(target, bytes, toDisk: true);
            }
        }

        /// Ends the restore: the plan (or journal) goes first, so it is never carried out again, then the staging folder.
        private string Drop(string line)
        {
            try
            {
                beforeStep();
                File.Delete(PlanPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"{line}; its plan could not be removed ({ex.Message})"; // the staging folder stays, so a journal can still be finished
            }
            Remove(_staging);
            return line;
        }

        private string Fresh(string name)
        {
            var path = Path.Combine(_staging, name);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true); // left by a start stopped while preparing: no store was changed
            Directory.CreateDirectory(path);
            return path;
        }
    }

    private static void WritePlan(string staging, Plan plan) =>
        AppDataStore.WriteAtomically(Path.Combine(staging, PlanName), JsonSerializer.SerializeToUtf8Bytes(plan), toDisk: true);

    private static string? Hash(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));

    private static bool IsPlainName(string name) => name is { Length: > 0 } and not ("." or "..") && Path.GetFileName(name) == name;

    private static JsonNode? Parse(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (JsonException ex)
        {
            AppDataStore.AppendLog(Guard.LogFile, $"Restore: a damaged file counts as none ({ex.Message})"); // the backup's is used
            return null;
        }
    }

    private static string Clear(string folder)
    {
        var staging = Path.Combine(folder, PendingFolder);
        Remove(staging);
        Directory.CreateDirectory(staging);
        return staging;
    }

    private static void Remove(string staging)
    {
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Its plan is already gone, so the files left behind are never applied; the next stage clears them first.
            AppDataStore.AppendLog(Guard.LogFile, $"Restore: the staging folder stays for now ({ex.Message})");
        }
    }
}
