namespace OmniControl.App.Shared.Ui.Restore;

/// What a page offers to "Restore defaults": how it is described, whether it already is at its defaults, a copy of its settings taken
/// before any change, the reset itself and the undo. Every reset goes through the page's own services, so the app reacts at once.
public interface IRestorable
{
    /// Stable key: the start of the page's <c>&lt;key&gt;-before-restore.json</c> and of its action id.
    string Key { get; }

    /// The page's name (its title): "Restore Displays defaults".
    string Name { get; }

    /// One plain sentence (or two) saying what is put back and what is left alone, for the question.
    string Scope { get; }

    /// A stronger line the question adds when the reset deletes something ("Deletes your custom rules"), else null.
    string? Warning => null;

    /// Why the reset cannot run now (one short sentence), or null when it can.
    string? Unavailable => null;

    /// True when the reset would change nothing (the button is greyed with "Already at the defaults").
    bool IsAtDefaults { get; }

    /// The page's current settings as text, taken before anything changes.
    string CaptureSnapshot();

    /// Puts the defaults in place; false when some of it could not be done.
    Task<bool> RestoreAsync();

    /// Puts the settings in <paramref name="snapshot"/> (from <see cref="CaptureSnapshot"/>) back; false when some of it could not be done.
    Task<bool> UndoAsync(string snapshot);
}
