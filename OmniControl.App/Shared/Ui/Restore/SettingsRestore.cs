using System.Text.Json;

namespace OmniControl.App.Shared.Ui.Restore;

/// An <see cref="IRestorable"/> over one settings value of type <typeparamref name="T"/>: the page says how to read what it has now, what
/// the defaults are and how to put a value in place (through its services); the comparison, the copy and the undo are the same for every page.
public sealed class SettingsRestore<T> : IRestorable where T : class
{
    private readonly Func<string> _scope;
    private readonly Func<T> _current, _defaults;
    private readonly Func<T, Task<bool>> _apply;
    private readonly Func<T, T, bool> _same;
    private readonly Func<T, bool>? _atDefaults;
    private readonly Func<string?> _unavailable, _warning;

    /// <param name="current">What the page has now.</param>
    /// <param name="defaults">What the defaults are, worked out from what it has now (a page may keep some of it).</param>
    /// <param name="apply">Puts a value in place through the page's services; false when some of it could not be done.</param>
    /// <param name="same">Whether the current value counts as the defaults (default: the same JSON).</param>
    /// <param name="atDefaults">Decides from the current value alone (when working out the defaults is costly); used instead of <paramref name="same"/>.</param>
    public SettingsRestore(string key, string name, Func<string> scope, Func<T> current, Func<T> defaults, Func<T, Task<bool>> apply,
        Func<T, T, bool>? same = null, Func<string?>? unavailable = null, Func<string?>? warning = null, bool hasOwnHotkey = false,
        Func<T, bool>? atDefaults = null)
    {
        (Key, Name, _scope, _current, _defaults, _apply) = (key, name, scope, current, defaults, apply);
        _same = same ?? ((a, b) => Json(a) == Json(b));
        _unavailable = unavailable ?? (() => null);
        _warning = warning ?? (() => null);
        HasOwnHotkey = hasOwnHotkey;
        _atDefaults = atDefaults;
    }

    public string Key { get; }
    public string Name { get; }
    public string Scope => _scope();
    public string? Warning => _warning();
    public string? Unavailable => _unavailable();
    public bool HasOwnHotkey { get; }
    public bool IsAtDefaults => _atDefaults is { } alone ? alone(_current()) : _same(_current(), _defaults());

    public string CaptureSnapshot() => Json(_current());

    public Task<bool> RestoreAsync() => _apply(_defaults());

    public Task<bool> UndoAsync(string snapshot) =>
        JsonSerializer.Deserialize<T>(snapshot) is { } before ? _apply(before) : Task.FromResult(false);

    private static string Json(T value) => JsonSerializer.Serialize(value);
}
