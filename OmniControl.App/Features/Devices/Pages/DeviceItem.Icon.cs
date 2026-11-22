using Avalonia.Media.Imaging;
using OmniControl.App.Features.Devices;
using OmniControl.App.Features.Icons;

namespace OmniControl.App.Features.Devices.Pages;

// The device's icon: an icon of the library or of the user's own (the first
// method's template's, or the plug, until the user picks one through the shared icon picker), or a picture picked before the library
// existed; lit in its colour while the device reads on, neutral otherwise.
public sealed partial class DeviceItem
{
    private static readonly IReadOnlyList<string> DeviceSuggestions = new[] { IconLibrary.Bulb, IconLibrary.Plug, "lamp" };

    private string IconValue => _service.IconOf(Record);

    /// What the square draws (null while an earlier picture shows instead).
    public IconImage? IconShape => IconBitmap is null ? IconResources.Find(IconValue) : null;

    public string IconName => IconLibrary.Find(IconValue)?.Name ?? "Your icon";

    /// The device reads on: the square takes its colour.
    public bool IsLit => _runtime.IsOn == true;

    public IBrush IconInk => new SolidColorBrush(DeviceIcons.ColourOf(Record.IconColour).Color);
    public IBrush IconWash => new SolidColorBrush(DeviceIcons.ColourOf(Record.IconColour).Color, Palette.IconWashOpacity);

    /// A picture picked for the device before the icon library (kept working), or null.
    public Bitmap? IconBitmap { get; private set; }
    public bool HasImage => IconBitmap is not null;
    public bool ShowsGlyph => IconBitmap is null && !IsLit;
    public bool IsLitGlyph => IconBitmap is null && IsLit;

    /// The icon pop-down's colour dots (<see cref="DeviceIcons.Colours"/>, the device's own ringed), fresh at each opening; a dot's pick colours the icon.
    public IReadOnlyList<SwatchItem> IconColourDots()
    {
        var chosen = DeviceIcons.ColourOf(Record.IconColour);
        List<SwatchItem> dots = null!;
        dots = DeviceIcons.Colours.Select((c, i) => new SwatchItem(new SolidColorBrush(c.Color), new RelayCommand(() =>
        {
            PickColour(c);
            for (var j = 0; j < dots.Count; j++) dots[j].IsSelected = j == i; // the pop-down stays open: the ring moves to the pick
        }), c.Name) { IsSelected = c == chosen }).ToList();
        return dots;
    }

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
                try
                {
                    using var file = File.OpenRead(path);
                    IconBitmap = IconPictures.Decode(file, "DeviceIconSize", fill: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    _report("The device's picture couldn't be read, so its drawn icon shows."); // a broken or odd file: the glyph stands in
                }
            }
        }
        foreach (var property in new[] { nameof(IconShape), nameof(IconName), nameof(IsLit), nameof(IconInk), nameof(IconWash), nameof(IconBitmap), nameof(HasImage), nameof(ShowsGlyph), nameof(IsLitGlyph) })
            OnPropertyChanged(property);
    }

    /// "Change icon…": the shared icon picker; its answer is the device's icon (Default: its template's again), shown on its actions, state and the tray too.
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

    private void PickColour(DeviceColour colour)
    {
        Record.IconColour = colour.Id;
        _service.Edited();
        RefreshIcon();
    }
}
