namespace OmniControl.App.Features.Screenshot;

/// What the shared save dialog saves: a picture or a recording.
public enum SaveKind { Screenshot, Recording }

/// The one description of each kind for the save dialog: its title, which of the action's
/// settings hold its folder and its "Ask where to save" switch, and the Discard question (also asked by the recording bar's X).
public static class SaveKinds
{
    public static string Title(SaveKind kind) => kind == SaveKind.Recording ? "Save recording" : "Save screenshot";

    /// The folder the action saves this kind to now (its "Save folder" setting, or the default).
    public static string Folder(SaveKind kind, ScreenshotOptions options) => kind == SaveKind.Recording ? options.VideoFolderInUse : options.FolderInUse;

    /// True while the action's "Ask where to save" switch for this kind is on.
    public static bool Asks(SaveKind kind, ScreenshotOptions options) => kind == SaveKind.Recording ? options.AskVideoFolder : options.AskFolder;

    /// A saved folder becomes the one the dialog offers next time: the action's "Save folder" setting (the one place it is kept).
    public static void RememberFolder(SaveKind kind, ScreenshotOptions options, string folder)
    {
        if (kind == SaveKind.Recording) options.VideoFolder = folder;
        else options.Folder = folder;
    }

    /// "Always save here, don't ask": the folder is remembered and the action's "Ask where to save" goes off.
    public static void AlwaysSaveHere(SaveKind kind, ScreenshotOptions options, string folder)
    {
        RememberFolder(kind, options, folder);
        if (kind == SaveKind.Recording) options.AskVideoFolder = false;
        else options.AskFolder = false;
    }

    /// The question before anything of this kind is thrown away (the dialog's Discard, the recording bar's X, Esc twice, the emergency key).
    public static DialogMessage DiscardQuestion(SaveKind kind) => kind == SaveKind.Recording
        ? new DialogMessage("Discard this recording?", "The video will not be saved.", "Discard", Danger: true, Cancel: "Keep recording")
        : new DialogMessage("Discard this screenshot?", "The picture will not be saved.", "Discard", Danger: true, Cancel: "Keep screenshot");

    /// The same question once Stop has not finished within <c>Timing.RecordingStopWait</c>: the way out that always works.
    public static DialogMessage ForceCloseQuestion { get; } =
        new("Discard this recording?", "The video will not be saved.", "Force close", Danger: true, Cancel: "Keep recording");
}
