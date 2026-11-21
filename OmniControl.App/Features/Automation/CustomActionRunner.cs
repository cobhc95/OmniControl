using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Shared.Scripts;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Automation;

/// Runs a <see cref="CustomAction"/> or a saved <see cref="Script"/>: always as the normal user (never elevated), commands and scripts hidden, stopped after
/// <see cref="Timing.CustomActionTimeout"/>, failures returned as one plain sentence (the details go to the log). Programs, files,
/// folders and addresses go through the <see cref="MouseActionRunner"/>, so the launch limiter protects them too.
public sealed class CustomActionRunner
{
    private static readonly string[] ScriptExtensions = { ".ps1", ".bat", ".cmd" };
    private readonly MouseActionRunner _launcher;
    private readonly Action<string> _log;
    private readonly LaunchLimiter _scripts = new();

    public CustomActionRunner(MouseActionRunner launcher, Action<string> log)
    {
        _launcher = launcher;
        _log = log;
    }

    /// Null when it worked (or was started), otherwise what went wrong.
    public async Task<string?> RunAsync(CustomAction action)
    {
        var name = action.Name.Length > 0 ? action.Name : "A custom action";
        _log($"custom action \"{name}\" runs ({action.Kind})");
        var problem = await RunKindAsync(action);
        if (problem is not null) _log($"custom action \"{name}\": {problem}");
        return problem is null ? null : $"{name} didn't finish: {problem}";
    }

    /// Runs a saved script the same way as a custom action's script: its text goes to a temporary file of its language, which is run and
    /// deleted afterwards. <paramref name="python"/> is the interpreter found (null: none). Null when it worked, otherwise what went wrong.
    public async Task<string?> RunScriptAsync(Script script, string? python)
    {
        var name = script.Name.Length > 0 ? script.Name : "A script";
        _log($"script \"{name}\" runs ({script.Language})");
        var problem = await RunScriptFileAsync(script, python);
        if (problem is not null) _log($"script \"{name}\": {problem}");
        return problem is null ? null : $"{name} didn't finish: {problem}";
    }

    private async Task<string?> RunScriptFileAsync(Script script, string? python)
    {
        if (script.Body.Trim().Length == 0) return "it is empty";
        if (script.Language == ScriptLanguage.Python && python is null) return "no installed Python was found";
        if (!_scripts.TryAcquire()) return "too many runs in a short time, so this one was skipped";
        var path = Path.Combine(Path.GetTempPath(), $"omnicontrol-{Guid.NewGuid():N}{ScriptLanguages.ExtensionOf(script.Language)}");
        FileStream? hold = null;
        try
        {
            await File.WriteAllTextAsync(path, script.Body);
            // Held open for reading only while it runs: nothing else may change or delete it between this check and the run.
            hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(hold, leaveOpen: true);
            if (await reader.ReadToEndAsync() != script.Body)
            {
                hold.Dispose();
                Remove(path);
                return "its temporary file was changed before it ran";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hold?.Dispose();
            Remove(path);
            return $"couldn't write its temporary file: {ex.Message}";
        }
        try
        {
            var (file, arguments) = script.Language switch
            {
                // A .ps1 still obeys the machine's script policy, which OmniControl does not override (as for a custom action's script file).
                ScriptLanguage.PowerShell => ScriptRunner.CommandLine(ScriptRunner.PowerShell, $"& '{path.Replace("'", "''")}'"),
                ScriptLanguage.Batch => ScriptRunner.CommandLine(ScriptRunner.Cmd, path), // the argument list quotes a path with spaces
                _ => (python!, new[] { path }),
            };
            return await ScriptRunner.RunAsync(file, arguments, hidden: true, Timing.CustomActionTimeout);
        }
        finally
        {
            hold?.Dispose();
            Remove(path);
        }
    }

    private void Remove(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"couldn't delete the temporary script {path}: {ex.Message}"); // Windows clears the temp folder in time
        }
    }

    private async Task<string?> RunKindAsync(CustomAction action)
    {
        var target = action.Target.Trim();
        if (target.Length == 0) return "it has nothing to run yet";
        switch (action.Kind)
        {
            case CustomKind.Command:
                return await ScriptRunner.RunAsync(ScriptRunner.PowerShell, target, hidden: true, Timing.CustomActionTimeout);
            case CustomKind.ScriptFile:
                if (!File.Exists(target)) return $"the file {target} was not found";
                if (!ScriptExtensions.Contains(Path.GetExtension(target).ToLowerInvariant())) return "it is not a script (.ps1, .bat or .cmd)";
                // PowerShell runs all three kinds; its execution policy is bypassed for this one process only.
                return await ScriptRunner.RunAsync(ScriptRunner.PowerShell, $"& '{target.Replace("'", "''")}' {action.Arguments}", hidden: true, Timing.CustomActionTimeout);
            case CustomKind.Url:
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "the address must start with http:// or https://";
                Open(target, string.Empty);
                return null;
            case CustomKind.Keys:
                // Off the UI thread: a sequence waits between its shortcuts. The runner reports unknown keys and its rate limit itself.
                await Task.Run(() => _launcher.Run(MouseAction.From($"{MouseActionCatalog.Keys}:{target}", new Dictionary<string, string>()), new MouseEvent(MouseButton.Left, true, 0, 0)));
                return null;
            default:
                Open(target, action.Kind == CustomKind.Program ? action.Arguments : string.Empty);
                return null;
        }
    }

    private void Open(string target, string arguments)
    {
        var values = arguments.Length == 0 ? new Dictionary<string, string>() : new Dictionary<string, string> { [MouseOptions.Arguments.Key] = arguments };
        _launcher.Run(MouseAction.From($"{MouseActionCatalog.Run}:{target}", values), new MouseEvent(MouseButton.Left, true, 0, 0));
    }
}
