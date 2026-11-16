using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Devices;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Devices;

/// <summary>One icon in the icon pop-down's grid: its glyph, its name and whether the device shows it.</summary>
public sealed record IconChoice(string Name, Geometry? Data, bool IsSelected, IRelayCommand PickCommand);

// The device's icon (docs/mockups/device-icons.html): a drawn glyph from the master list (the first method's template's, or the plug, until the user
// picks one) or the user's own picture; lit in its colour while the device reads on, neutral otherwise.
public sealed partial class DeviceItem
{
    private DeviceIcon Icon => DeviceIcons.Of(Record.Icon) ?? DeviceIcons.Of(Record.Methods.FirstOrDefault() is { IsSimulation: false } first ? _service.ProfileOf(first).Icon : DeviceIcons.Plug)!;

    public Geometry? IconData => GlyphOf(Icon);

    public string IconName => Icon.Name;

    /// <summary>The device reads on: the square takes its colour.</summary>
    public bool IsLit => _runtime.IsOn == true;

    public IBrush IconInk => new SolidColorBrush(DeviceIcons.ColourOf(Record.IconColour).Color);
    public IBrush IconWash => new SolidColorBrush(DeviceIcons.ColourOf(Record.IconColour).Color, Palette.IconWashOpacity);

    /// <summary>The user's own picture, or null.</summary>
    public Bitmap? IconBitmap { get; private set; }
    public bool HasImage => IconBitmap is not null;
    public bool ShowsGlyph => IconBitmap is null && !IsLit;
    public bool IsLitGlyph => IconBitmap is null && IsLit;

    /// <summary>The icon pop-down's grid: every icon of the master list, the device's one selected.</summary>
    public IReadOnlyList<IconChoice> IconChoices => DeviceIcons.All.Select(i => new IconChoice(i.Name, GlyphOf(i), i.Id == Icon.Id && !HasImage, new RelayCommand(() => PickIcon(i.Id)))).ToList();

    public IReadOnlyList<Color> IconColours => DeviceIcons.Colours.Select(c => c.Color).ToList();
    public int IconColourIndex => DeviceIcons.Colours.ToList().IndexOf(DeviceIcons.ColourOf(Record.IconColour));

    private static Geometry? GlyphOf(DeviceIcon icon) =>
        Application.Current?.TryFindResource(icon.Resource, out var found) == true ? found as Geometry : null;

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
        foreach (var property in new[] { nameof(IconData), nameof(IconName), nameof(IsLit), nameof(IconInk), nameof(IconWash), nameof(IconBitmap), nameof(HasImage), nameof(ShowsGlyph), nameof(IsLitGlyph), nameof(IconChoices), nameof(IconColourIndex) })
            OnPropertyChanged(property);
    }

    private void PickIcon(string id)
    {
        Record.Icon = id;
        if (Record.IconImage.Length > 0) _service.ClearOwnImage(Record);
        _service.Edited();
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

    /// <summary>The user picked a picture (the view asked where): it is copied into the app's data and shown in the square.</summary>
    public void PickOwnImage(string path)
    {
        if (_service.SetOwnImage(Record, path) is { } problem) _report(problem);
        RefreshIcon();
    }
}
