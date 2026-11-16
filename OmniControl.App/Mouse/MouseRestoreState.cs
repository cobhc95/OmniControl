using System.Text.Json;

namespace OmniControl.App.Mouse;

/// <summary>
/// What "Restore defaults" on the Mouse and Trackpad page covers (pure): every mapping, layer and option (<see cref="MouseSettings"/>), and
/// Windows' pointer settings and pointers, which go back to what the user had before OmniControl first changed one (the pointer backup).
/// <see cref="Pointer"/> is null while OmniControl has never changed a pointer setting: there is then nothing to put back.
/// </summary>
public sealed class MouseRestoreState
{
    public MouseSettings Mouse { get; set; } = new();
    public PointerOriginal? Pointer { get; set; }

    /// <summary>The defaults: the new-install mouse settings, and the pointer backup as the pointer part (when there is one to put back).</summary>
    public static MouseRestoreState DefaultsOf(MouseRestoreState current, PointerOriginal? original) =>
        new() { Mouse = MouseSettings.Defaults(), Pointer = current.Pointer is null ? null : original };

    public static bool Same(MouseRestoreState a, MouseRestoreState b) =>
        Json(a.Mouse) == Json(b.Mouse) && PointerSame(a.Pointer, b.Pointer);

    /// <summary>Equal values and pointers (the day the copy was made does not count, nor the order of the entries).</summary>
    private static bool PointerSame(PointerOriginal? a, PointerOriginal? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.Values.Count == b.Values.Count && a.Values.All(v => b.Values.TryGetValue(v.Key, out var other) && other == v.Value)
            && Json(a.Cursors) == Json(b.Cursors);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
