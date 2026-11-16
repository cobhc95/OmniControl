using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OmniControl.App.Devices;
using OmniControl.App.Icons;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Devices;

// The device's icon (docs/mockups/device-icons.html, docs/mockups/icon-library.html): an icon of the library or of the user's own (the first
// method's template's, or the plug, until the user picks one through the shared icon picker), or a picture picked before the library
// existed; lit in its colour while the device reads on, neutral otherwise.
public sealed partial class DeviceItem
{
    private static readonly IReadOnlyList<string> DeviceSuggestions = new[] { IconLibrary.Bulb, IconLibrary.Plug, "lamp" };

    private string IconValue => _service.IconOf(Record);

    /// <summary>What the square draws (null while an earlier picture shows instead).</summary>
    public IconImage? IconShape => IconBitmap is null ? IconResources.Find(IconValue) : null;

    public string IconName => IconLibrary.Find(IconValue)?.Name ?? "Your icon";

    /// <summary>The device reads on: the square takes its colour.</summary>
    public bool IsLit => _runtime.IsOn == true;

    public IBrush IconInk => new SolidColorBrush(DeviceIcons.ColourOf(Record.IconColour).Color);
    public IBrush IconWash => new SolidColorBrush(DeviceIcons.ColourOf(Record.IconColour).Color, Palette.IconWashOpacity);

    /// <summary>A picture picked for the device before the icon library (kept working), or null.</summary>
    public Bitmap? IconBitmap { get; private set; }
    public bool HasImage => IconBitmap is not null;
    public bool ShowsGlyph => IconBitmap is null && !IsLit;
    public bool IsLitGlyph => IconBitmap is null && IsLit;

    public IReadOnlyList<Color> IconColours => DeviceIcons.Colours.Select(c => c.Color).ToList();
    public int IconColourIndex => DeviceIcons.Colours.ToList().IndexOf(DeviceIcons.ColourOf(Record.IconColour));

    private string _shownImage = string.Empty;

    private void RefreshIcon()
    {
        var path = DeviceService.OwnImagePath(Record) ?? string.Empty;
        if (path != _shownImage)
        {
            IconBitmap?.Dispose();
            IconBitmap = null;
            _shownImage = path;
            if (path.Length > 0)
            {
                try { IconBitmap = new Bitmap(path); }
                catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
                {
                    _report("The device's picture couldn't be read, so its drawn icon shows."); // a broken or odd file: the glyph stands in
                }
            }
        }
        foreach (var property in new[] { nameof(IconShape), nameof(IconName), nameof(IsLit), nameof(IconInk), nameof(IconWash), nameof(IconBitmap), nameof(HasImage), nameof(ShowsGlyph), nameof(IsLitGlyph), nameof(IconColourIndex) })
            OnPropertyChanged(property);
    }

    /// <summary>"Change icon…": the shared icon picker; its answer is the device's icon (Default: its template's again), shown on its actions, state and the tray too.</summary>
    public async Task ChangeIconAsync(Visual from)
    {
        var request = new IconRequest(Record.Name, Record.Icon.Length > 0 ? Record.Icon : null, IconLibrary.Suggestions(DeviceSuggestions, IconValue, Record.Name));
        if (await _chooseIcon(from, request) is not { } pick) return;
        Record.Icon = pick.Value ?? string.Empty;
        if (Record.IconImage.Length > 0) _service.ClearOwnImage(Record);
        _service.Edited();
        _service.IconChanged(Record);
        RefreshIcon();
    }

    /// <summary>The colour dot at <paramref name="index"/> of <see cref="DeviceIcons.Colours"/> was picked.</summary>
    public void PickColour(int index)
    {
        if (index < 0 || index >= DeviceIcons.Colours.Count) return;
        Record.IconColour = DeviceIcons.Colours[index].Id;
        _service.Edited();
        RefreshIcon();
    }
}
