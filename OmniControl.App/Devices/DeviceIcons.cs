using Avalonia.Media;
using OmniControl.App.Services;

namespace OmniControl.App.Devices;

/// <summary>One icon a device can show: its id (saved on the device), its name (the tooltip) and the glyph's resource in Styles/Brushes.axaml.</summary>
public sealed record DeviceIcon(string Id, string Name, string Resource);

/// <summary>One colour a device's icon lights up in while the device is on.</summary>
public sealed record DeviceColour(string Id, string Name, Color Color);

/// <summary>The master lists of device icons and their colours (docs/mockups/device-icons.html); the icon pop-down and every icon square read these.</summary>
public static class DeviceIcons
{
    public const string Bulb = "bulb", Plug = "plug";

    public static IReadOnlyList<DeviceIcon> All { get; } = new[]
    {
        new DeviceIcon(Bulb, "Bulb", "DeviceBulbIcon"),
        new DeviceIcon(Plug, "Plug", "DevicePlugIcon"),
        new DeviceIcon("lamp", "Lamp", "DeviceLampIcon"),
        new DeviceIcon("fan", "Fan", "DeviceFanIcon"),
        new DeviceIcon("tv", "TV", "DeviceTvIcon"),
        new DeviceIcon("desktop", "Computer", "DeviceDesktopIcon"),
        new DeviceIcon("coffee", "Coffee maker", "DeviceCoffeeIcon"),
        new DeviceIcon("thermometer", "Thermometer", "DeviceThermometerIcon"),
        new DeviceIcon("speaker", "Speaker", "DeviceSpeakerIcon"),
        new DeviceIcon("strip", "Power strip", "DeviceStripIcon"),
        new DeviceIcon("heater", "Heater", "DeviceHeaterIcon"),
        new DeviceIcon("switch", "Wall switch", "DeviceSwitchIcon"),
    };

    public static IReadOnlyList<DeviceColour> Colours { get; } = new[]
    {
        new DeviceColour("amber", "Amber", Palette.Caution),
        new DeviceColour("blue", "Blue", Palette.Cable),
        new DeviceColour("green", "Green", Palette.On),
        new DeviceColour("grey", "Grey", Palette.OsdGrey),
    };

    /// <summary>The icon with this id; null when it is not known.</summary>
    public static DeviceIcon? Of(string? id) => All.FirstOrDefault(i => i.Id == id);

    /// <summary>The colour with this id; the first (amber) when it is not known.</summary>
    public static DeviceColour ColourOf(string? id) => Colours.FirstOrDefault(c => c.Id == id) ?? Colours[0];

    /// <summary>The folder (in the app's data) where a device's own image is kept, copied there when it is picked.</summary>
    public static string ImageFolder => AppDataStore.PathOf("device-icons");
}
