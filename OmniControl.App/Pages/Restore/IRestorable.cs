namespace OmniControl.App.Pages.Restore;

/// <summary>
/// What a page offers to "Restore defaults": how it is described, whether it already is at its defaults, a copy of its settings taken
/// before any change, the reset itself and the undo. Every reset goes through the page's own services, so the app reacts at once.
/// </summary>
public interface IRestorable
{
    /// <summary>Stable key: the start of the page's <c>&lt;key&gt;-before-restore.json</c> and of its action id.</summary>
    string Key { get; }

    /// <summary>The page's name (its title): "Restore Displays defaults".</summary>
    string Name { get; }

    /// <summary>One plain sentence (or two) saying what is put back and what is left alone, for the question.</summary>
    string Scope { get; }

    /// <summary>A stronger line the question adds when the reset deletes something ("Deletes your custom rules"), else null.</summary>
    string? Warning => null;

    /// <summary>Why the reset cannot run now (one short sentence), or null when it can.</summary>
    string? Unavailable => null;

    /// <summary>True when the reset would change nothing (the button is greyed with "Already at the defaults").</summary>
    bool IsAtDefaults { get; }

    /// <summary>True when the page registers its own action for this reset, so the shell adds none.</summary>
    bool HasOwnHotkey => false;

    /// <summary>The page's current settings as text, taken before anything changes.</summary>
    string CaptureSnapshot();

    /// <summary>Puts the defaults in place; false when some of it could not be done.</summary>
    Task<bool> RestoreAsync();

    /// <summary>Puts the settings in <paramref name="snapshot"/> (from <see cref="CaptureSnapshot"/>) back; false when some of it could not be done.</summary>
    Task<bool> UndoAsync(string snapshot);
}
