using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmniControl.App.Services.Backup;

/// <summary>How an import or a wipe changes the saved stores.</summary>
public enum RestoreMode
{
    /// <summary>Every backed-up store becomes the backup's (a store the backup lacks goes back to its defaults).</summary>
    Replace,

    /// <summary>A store that can merge gains what it lacks; one that can't is replaced by the backup's; a store the backup lacks is kept.</summary>
    Merge,

    /// <summary>Every saved store and undo copy is deleted: factory settings.</summary>
    Wipe,
}

/// <summary>
/// An import or a wipe is only staged while OmniControl runs (its pages hold the settings and save them as they close), and carried out
/// at the next start, before anything is loaded (<see cref="ApplyPending"/>). The staged files sit in a folder of their own beside the
/// stores, with a plan saying how to apply them. Safety journals (<see cref="StoreKind.Safety"/>) are never touched, and what a backup
/// may not set (<see cref="DataStore.NotImported"/>) keeps the value here. Works on any folder, so tests use a temporary one.
/// </summary>
public static class PendingRestore
{
    private const string PendingFolder = "pending-restore";
    private const string PlanName = "plan.json";

    private sealed record Plan(RestoreMode Mode);

    public static void Stage(string folder, BackupContents contents, RestoreMode mode)
    {
        var staging = Clear(folder);
        foreach (var (name, text) in contents.Files) File.WriteAllText(Path.Combine(staging, name), text);
        File.WriteAllText(Path.Combine(staging, PlanName), JsonSerializer.Serialize(new Plan(mode)));
    }

    public static void StageWipe(string folder)
    {
        var staging = Clear(folder);
        File.WriteAllText(Path.Combine(staging, PlanName), JsonSerializer.Serialize(new Plan(RestoreMode.Wipe)));
    }

    /// <summary>
    /// Carries out a staged import or wipe, if there is one, and removes it; gives a line for the log (null when nothing was staged).
    /// A damaged plan is dropped without changing anything.
    /// </summary>
    public static string? ApplyPending(string folder)
    {
        var staging = Path.Combine(folder, PendingFolder);
        var planPath = Path.Combine(staging, PlanName);
        if (!File.Exists(planPath)) return null;
        try
        {
            var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(planPath));
            File.Delete(planPath); // first, so a restore (above all a wipe) is never carried out twice, even if the folder can't be removed
            if (plan is null) return "a staged restore had no plan; nothing was changed";
            if (plan.Mode != RestoreMode.Merge) DeleteStores(folder, staging);
            if (plan.Mode == RestoreMode.Wipe) File.Delete(Path.Combine(folder, BackupFile.BeforeImportName));
            if (plan.Mode != RestoreMode.Wipe)
                foreach (var store in DataStores.BackedUp)
                    Apply(folder, staging, store, plan.Mode);
            return $"applied the staged {plan.Mode.ToString().ToLowerInvariant()}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return $"the staged restore failed: {ex.Message}";
        }
        finally
        {
            Remove(staging);
        }
    }

    /// <summary>
    /// Deletes every saved store and undo copy (wipe, or before a full replace); safety journals stay. A store the backup holds is left for
    /// <see cref="Apply"/> to overwrite, so it can still read what this store keeps of its own (the approvals).
    /// </summary>
    private static void DeleteStores(string folder, string staging)
    {
        foreach (var store in DataStores.All.Where(s => s.Kind != StoreKind.Safety && !File.Exists(Path.Combine(staging, s.FileName))))
            foreach (var file in Directory.EnumerateFiles(folder, store.FileName))
                File.Delete(file);
    }

    private static void Apply(string folder, string staging, DataStore store, RestoreMode mode)
    {
        var incomingPath = Path.Combine(staging, store.FileName);
        if (!File.Exists(incomingPath)) return;
        var target = Path.Combine(folder, store.FileName);
        var here = File.Exists(target) ? Parse(File.ReadAllText(target)) : null;
        var incoming = JsonMerge.KeepOwn(Parse(File.ReadAllText(incomingPath)), here, store.NotImported ?? Array.Empty<string>());
        var result = mode == RestoreMode.Merge && store.Mergeable && here is not null ? JsonMerge.Merge(here, incoming) : incoming;
        if (result is not null) File.WriteAllText(target, result.ToJsonString());
    }

    private static JsonNode? Parse(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; } // a damaged store here counts as none: the backup's is used
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
        }
    }
}
