using OmniControl.App.Services;
using OmniControl.Core.Engine.Pointer;

namespace OmniControl.App.Mouse;

/// <summary>The user's own pointer settings as they were before OmniControl first changed one: every value (by <see cref="PointerValue"/> name) and the pointers.</summary>
public sealed record PointerOriginal(Dictionary<string, int> Values, CursorSnapshot Cursors, DateTime SavedAt);

/// <summary>
/// Owns <c>pointer-settings-original.json</c>: written once, just before OmniControl's first change to a Windows pointer setting, and
/// never overwritten, so "Restore my original values" always means the values the user had before OmniControl touched them.
/// </summary>
internal sealed class PointerBackup
{
    private const string FileName = "pointer-settings-original.json";

    private PointerOriginal? _original = AppDataStore.Load<PointerOriginal>(FileName);

    /// <summary>The saved originals, or null when OmniControl has changed nothing yet.</summary>
    public PointerOriginal? Original => _original;

    /// <summary>Saves <paramref name="capture"/>'s answer unless an original is already kept. Call before the first write.</summary>
    public void EnsureSaved(Func<PointerOriginal> capture)
    {
        if (_original is not null) return;
        _original = capture();
        AppDataStore.Save(FileName, _original);
    }
}
