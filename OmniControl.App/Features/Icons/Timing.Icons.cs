namespace OmniControl.App.Shared.Services;

// The intervals and limits of the icon picker; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// The icon picker shows this many cells at once after a search; the rest follow in the next frame so the first results appear immediately.
    public const int IconPickerFirstCells = 48;
}
