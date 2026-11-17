using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// One display's Orientation card: the four ways Windows can turn it, as a gallery. Windows does the turning (Core's
/// <see cref="IDisplayRotation"/>), so it works for every display, the built-in one included. A choice is applied at once on a worker
/// thread; a refusal is one plain sentence in the status line, the detail goes to <see cref="LogFile"/>, and the card goes back to what
/// Windows says the display is now.
/// </summary>
internal sealed class DisplayOrientationCard
{
    private const string LogFile = "display-log.txt";
    private const string Unknown = "Windows would not say how this display is turned";

    private static readonly IReadOnlyList<string> Names = new[] { "Landscape", "Portrait", "Landscape (flipped)", "Portrait (flipped)" };

    private static readonly IReadOnlyList<string> ShortNames = new[] { "Landscape", "Portrait", "Landscape (flip)", "Portrait (flip)" };

    /// <summary>The orientation's name for a small tile (the tray panel's): each its own, so two tiles never read the same.</summary>
    public static string ShortTitle(DisplayOrientation orientation) => ShortNames[(int)orientation];

    /// <summary>What the orientation is called in the card and in the hotkeys' titles.</summary>
    public static string Title(DisplayOrientation orientation) => Names[(int)orientation];

    private readonly string? _deviceName;
    private readonly string _displayName;
    private readonly IDisplayRotation _rotation;
    private readonly Action<string> _report;
    private readonly SuggestedKeysSwitch? _suggestedKeys;
    private bool _disposed;

    public GalleryItem Card { get; }

    /// <param name="deviceName">Windows' name for the display (the one it turns); null when Windows lists none for it.</param>
    /// <param name="suggestedKeys">The switch for the orientation hotkeys' suggested keys, shown at the card's right; the card owns it.</param>
    public DisplayOrientationCard(string? deviceName, string displayName, IDisplayRotation rotation, Action<string> report, SuggestedKeysSwitch? suggestedKeys = null)
    {
        (_deviceName, _displayName, _rotation, _report, _suggestedKeys) = (deviceName, displayName, rotation, report, suggestedKeys);
        Card = new GalleryItem("Orientation", Names, 0, () => OrientationPreview.Build(Card!.Index), Apply)
        {
            Tip = "Turns the screen through Windows. Its width and height swap between landscape and portrait.",
            Extra = suggestedKeys,
        };
        Card.Availability.MakeUnavailable("Reading the display...");
    }

    /// <summary>Asks Windows how the display is turned now, off the UI thread, and fills the card in.</summary>
    public void Start() => Task.Run(() =>
    {
        var now = _deviceName is null ? null : _rotation.Read(_deviceName);
        Dispatcher.UIThread.Post(() => Show(now));
    });

    public void Dispose()
    {
        _disposed = true;
        _suggestedKeys?.Dispose();
    }

    /// <summary>Turns the display (a hotkey); nothing while the card is unavailable.</summary>
    public void Choose(DisplayOrientation orientation)
    {
        if (Card.Availability.IsAvailable) Card.Index = (int)orientation;
    }

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
        if (_deviceName is null) return;
        Task.Run(() =>
        {
            var result = _rotation.Set(_deviceName, (DisplayOrientation)index);
            if (result.Success) return;
            AppDataStore.AppendLog(LogFile, $"Turning {_displayName} ({_deviceName}) to {Names[index]} failed: {result.Message}");
            var now = _rotation.Read(_deviceName);
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                _report($"Windows would not turn {_displayName} to {Names[index].ToLowerInvariant()}. Details are in {LogFile}.");
                if (now is { } actual) Card.Load((int)actual);
            });
        });
    }
}
