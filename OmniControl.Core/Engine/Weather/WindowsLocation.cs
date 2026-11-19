using Windows.Devices.Geolocation;

namespace OmniControl.Core.Engine.Weather;

/// Where Windows' own location service says this PC is, asked only when the user picks "Use my location". Null when location is off for
/// desktop apps or refused; Windows may throw <see cref="UnauthorizedAccessException"/> or a COM error, which the caller words.
public static class WindowsLocation
{
    public static async Task<(double Latitude, double Longitude)?> FindAsync()
    {
        if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return null;
        var point = (await new Geolocator { DesiredAccuracy = PositionAccuracy.Default }.GetGeopositionAsync()).Coordinate.Point.Position;
        return (point.Latitude, point.Longitude);
    }
}
