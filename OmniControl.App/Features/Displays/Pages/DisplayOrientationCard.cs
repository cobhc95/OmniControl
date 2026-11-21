using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Icons;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays.Pages;

/// One display's Orientation card: the four ways Windows can turn it, as a gallery. Windows does the turning (Core's
/// <see cref="IDisplayRotation"/>), so it works for every display, the built-in one included. A choice is applied at once on a worker
/// thread; a refusal is one plain sentence in the status line, the detail goes to <see cref="LogFile"/>, and the card goes back to what
/// Windows says the display is now.
internal sealed class DisplayOrientationCard
{
    private const string LogFile = "display-log.txt";
    private const string CardReason = "chosen on the Orientation card";
    private const string Unknown = "Windows would not say how this display is turned";

    private static readonly IReadOnlyList<string> Names = new[] { "Landscape", "Portrait", "Landscape (flipped)", "Portrait (flipped)" };

    private static readonly IReadOnlyList<string> ShortNames = new[] { "Landscape", "Portrait", "Landscape (flip)", "Portrait (flip)" };

    /// The orientation's name for a small tile (the tray panel's): each its own, so two tiles never read the same.
    public static string ShortTitle(DisplayOrientation orientation) => ShortNames[(int)orientation];

    /// The orientation's icon: the portrait ones turn clockwise, so a Portrait tile never looks like a Landscape one.
    public static string Icon(DisplayOrientation orientation) =>
        orientation is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped ? IconLibrary.RotateClockwise : IconLibrary.Rotate;

    /// What the orientation is called in the card and in the hotkeys' titles.
    public static string Title(DisplayOrientation orientation) => Names[(int)orientation];

    private readonly DisplayInfo? _display;
    private readonly Func<DisplayInfo, string?> _resolve;
    private readonly string _displayName;
    private readonly IDisplayRotation _rotation;
    private readonly Action<string> _report;
    private readonly SuggestedKeysSwitch? _suggestedKeys;
    private bool _disposed;
    private string _reason = CardReason;

    public GalleryItem Card { get; }

    /// <param name="display">The display it turns, remembered by its device path; null when Windows lists none for it.</param>
    /// <param name="resolve">Asks Windows for the display's name right now (it can change when displays come and go); null: it is not attached, and nothing is turned.</param>
    /// <param name="suggestedKeys">The switch for the orientation hotkeys' suggested keys, shown at the card's right; the card owns it.</param>
    public DisplayOrientationCard(DisplayInfo? display, Func<DisplayInfo, string?> resolve, string displayName, IDisplayRotation rotation, Action<string> report, SuggestedKeysSwitch? suggestedKeys = null)
    {
        (_display, _resolve, _displayName, _rotation, _report, _suggestedKeys) = (display, resolve, displayName, rotation, report, suggestedKeys);
        Card = new GalleryItem("Orientation", Names, 0, () => OrientationPreview.Build(Card!.Index), Apply)
        {
            Tip = "Turns the screen through Windows. Its width and height swap between landscape and portrait.",
            Extra = suggestedKeys,
        };
        Card.Availability.MakeUnavailable("Reading the display...");
    }

    /// Asks Windows how the display is turned now, off the UI thread, and fills the card in.
    public void Start() => Guard.OffUi("Displays: reading the orientation", () =>
    {
        var now = CurrentName() is { } name ? _rotation.Read(name) : null;
        Dispatcher.UIThread.Post(() => Show(now));
    });

    public void Dispose()
    {
        _disposed = true;
        _suggestedKeys?.Dispose();
    }

    /// Turns the display (a hotkey); nothing while the card is unavailable.
    public void Choose(DisplayOrientation orientation)
    {
        if (!Card.Availability.IsAvailable) return;
        _reason = "a hotkey or action asked for it";
        Card.Index = (int)orientation;
    }

    /// The display's name in Windows now, asked fresh each time; null when it is not attached.
    private string? CurrentName() => _display is null ? null : _resolve(_display);

    private void Show(DisplayOrientation? now)
    {
        if (_disposed) return;
        if (now is null)
        {
            Card.Availability.MakeUnavailable(Unknown);
            return;
        }
        Card.Load((int)now.Value);
        Card.Availability.MakeAvailable();
    }

    private void Apply(int index)
    {
        if (_display is null) return;
        var reason = _reason;
        _reason = CardReason;
        Guard.OffUi("Displays: turning a display", () =>
        {
            var deviceName = CurrentName();
            if (deviceName is null)
            {
                AppDataStore.AppendLog(LogFile, $"Turning {_displayName} to {Names[index]} skipped ({reason}): Windows no longer lists this display, so nothing was turned.");
                Dispatcher.UIThread.Post(() => { if (!_disposed) _report($"{_displayName} is not attached, so it was not turned."); });
                return;
            }
            var result = _rotation.Set(deviceName, (DisplayOrientation)index);
            if (result.Success)
            {
                AppDataStore.AppendLog(LogFile, $"Turned {_displayName} ({deviceName}) to {Names[index]} and saved it to Windows' display settings: {reason}.");
                return;
            }
            AppDataStore.AppendLog(LogFile, $"Turning {_displayName} ({deviceName}) to {Names[index]} failed ({reason}): {result.Message}");
            var now = _rotation.Read(deviceName);
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                _report(Guard.SeeLog($"Windows would not turn {_displayName} to {Names[index].ToLowerInvariant()}.", LogFile));
                if (now is { } actual) Card.Load((int)actual);
            });
        });
    }
}
