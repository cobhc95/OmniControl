namespace OmniControl.App.Shared.Services;

// The intervals and limits of the app lists and their installed apps; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// The installed apps (the "Open ..." actions) are looked for again when the window is shown or a picker listing them opens, but not more often than this.
    public static readonly TimeSpan AppScanGap = TimeSpan.FromSeconds(30);
}
