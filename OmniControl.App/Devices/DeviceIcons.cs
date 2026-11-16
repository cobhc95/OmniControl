using Avalonia.Media;
using OmniControl.App.Services;

namespace OmniControl.App.Devices;

/// <summary>One colour a device's icon lights up in while the device is on.</summary>
public sealed record DeviceColour(string Id, string Name, Color Color);

/// <summary>
/// The master list of the colours a device's icon lights up in (docs/mockups/device-icons.html), and the folder of the pictures picked
/// for devices before the icon library. The icons themselves are the library's (<see cref="Icons.IconLibrary"/>); the ids saved on devices
/// before it are library ids too.
/// </summary>
public static class DeviceIcons
{
    public static IReadOnlyList<DeviceColour> Colours { get; } = new[]
    {
        new DeviceColour("amber", "Amber", Palette.Caution),
        new DeviceColour("blue", "Blue", Palette.Cable),
        new DeviceColour("green", "Green", Palette.On),
        new DeviceColour("grey", "Grey", Palette.OsdGrey),
    };

    /// <summary>The colour with this id; the first (amber) when it is not known.</summary>
    public static DeviceColour ColourOf(string? id) => Colours.FirstOrDefault(c => c.Id == id) ?? Colours[0];

    /// <summary>The folder (in the app's data) where a picture picked for a device before the icon library is kept.</summary>
    public static string ImageFolder => AppDataStore.PathOf("device-icons");
}
