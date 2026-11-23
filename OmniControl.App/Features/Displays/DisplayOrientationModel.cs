using OmniControl.App.Features.Icons;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays;

/// One display's orientation: the four ways Windows can turn it. Windows does the turning (Core's <see cref="IDisplayRotation"/>), so it
/// works for every display, the built-in one included. A choice is applied at once on a worker thread; a refusal is one plain sentence in
/// the status line, the detail goes to <see cref="LogFile"/>, and the orientation goes back to what Windows says the display is now.
internal sealed class DisplayOrientationModel
{
    private const string LogFile = LogFiles.Display;
    private const string CardReason = "chosen on the Orientation card";
    private const string Unknown = "Windows would not say how this display is turned";
    private const string Reading = "Reading the display...";

    /// The card's name (also what search finds it by).
    public const string Label = "Orientation";

    /// What the orientations are called on the card and in the hotkeys' titles.
    public static readonly IReadOnlyList<string> Names = new[] { "Landscape", "Portrait", "Landscape (flipped)", "Portrait (flipped)" };

    private static readonly IReadOnlyList<string> ShortNames = new[] { "Landscape", "Portrait", "Landscape (flip)", "Portrait (flip)" };

    /// The orientation's name for a small tile (the tray panel's): each its own, so two tiles never read the same.
    public static string ShortTitle(DisplayOrientation orientation) => ShortNames[(int)orientation];

    /// The orientation's icon: the portrait ones turn clockwise, so a Portrait tile never looks like a Landscape one.
    public static string Icon(DisplayOrientation orientation) =>
        orientation is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped ? IconLibrary.RotateClockwise : IconLibrary.Rotate;

    public static string Title(DisplayOrientation orientation) => Names[(int)orientation];

    private readonly DisplayInfo? _display;
    private readonly Func<DisplayInfo, string?> _resolve;
    private readonly string _displayName;
    private readonly IDisplayRotation _rotation;
    private readonly Action<string> _report;
    private bool _disposed;

    /// The orientation as an index of <see cref="Names"/>, and why it cannot be chosen now (null: it can).
    public LevelState State { get; } = new(0, 0, Reading);

    public DisplayOrientation? Current => State.IsAvailable ? (DisplayOrientation)State.Value : null;
    public string? DeviceName => _display?.DeviceName;

    /// The orientation or its availability changed (UI thread).
    public event Action? Changed;

    /// <param name="display">The display it turns, remembered by its device path; null when Windows lists none for it.</param>
    /// <param name="resolve">Asks Windows for the display's name right now (it can change when displays come and go); null: it is not attached, and nothing is turned.</param>
    public DisplayOrientationModel(DisplayInfo? display, Func<DisplayInfo, string?> resolve, string displayName, IDisplayRotation rotation, Action<string> report) =>
        (_display, _resolve, _displayName, _rotation, _report) = (display, resolve, displayName, rotation, report);

    public void Dispose() => _disposed = true;

    /// Turns the display (a hotkey); nothing while it cannot be chosen or is turned so already.
    public void Choose(DisplayOrientation orientation)
    {
        if (State.IsAvailable && State.Value != (int)orientation) Apply((int)orientation, "a hotkey or action asked for it");
    }

    /// Turns the display (the card).
    public void Set(int index) => Apply(index, CardReason);

    /// The display's name in Windows now, asked fresh each time; null when it is not attached.
    private string? CurrentName() => _display is null ? null : _resolve(_display);

    public void Show(DisplayOrientation? now)
    {
        if (_disposed || (Current == now && State.Reason != Reading)) return;
        if (now is { } value) (State.Value, State.Reason) = ((int)value, null);
        else State.Reason = Unknown;
        Changed?.Invoke();
    }

    private void Apply(int index, string reason)
    {
        if (_display is null) return;
        Guard.OffUi("Displays: turning a display", () =>
        {
            var deviceName = CurrentName();
            if (deviceName is null)
            {
                AppDataStore.AppendLog(LogFile, $"Turning {_displayName} to {Names[index]} skipped ({reason}): Windows no longer lists this display, so nothing was turned.");
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed) return;
                    Show(null);
                    _report($"{_displayName} is not attached, so it was not turned.");
                });
                return;
            }
            var result = _rotation.Set(deviceName, (DisplayOrientation)index);
            AppDataStore.AppendLog(LogFile, result.Success
                ? $"Turned {_displayName} ({deviceName}) to {Names[index]} and saved it to Windows' display settings: {reason}."
                : $"Turning {_displayName} ({deviceName}) to {Names[index]} failed ({reason}): {result.Message}");
            var now = _rotation.Read(deviceName);
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                if (!result.Success) _report(Guard.SeeLog($"Windows could not apply or save {Names[index].ToLowerInvariant()} on {_displayName}.", LogFile));
                Show(now);
            });
        });
    }
}
