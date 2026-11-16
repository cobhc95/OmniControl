using OmniControl.App.Audio;
using OmniControl.App.Pages.Volume.Easy.Flow;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>The words of "Restore defaults" (pure): the question, the confirmation line, the greyed reason and the undo notes.</summary>
public static class EasyRestore
{
    public const string AtDefaults = "Already at the defaults";
    public const string Done = "Audio defaults restored";
    public const string NoCopy = "Nothing to undo: the saved copy was not found.";
    public const string Undone = "Your earlier audio settings are back.";
    public const string Tip = "Remove all effects, combined devices, methods and routes";

    /// <summary>The one calm question.</summary>
    public static EasyConfirm Question { get; } = new("Restore audio defaults?", AudioDefaults.Scope + " You can undo this.", "Restore");
}
