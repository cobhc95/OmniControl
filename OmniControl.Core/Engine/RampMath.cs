using OmniControl.Core.Models;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>The arithmetic of a gamma ramp (pure, no Windows calls): brightness, contrast and colour temperature as three 256-entry curves.</summary>
internal static class RampMath
{
    private const int Steps = 256, Last = Steps - 1;
    private const double Full = ushort.MaxValue;

    /// <summary>A colour channel is never dimmed below this share of itself by the temperature, so no channel can vanish.</summary>
    private const double LeastChannel = 0.2;

    /// <summary>The ramp that changes nothing: entry i is i/255 of full scale.</summary>
    public static ushort[] Identity()
    {
        var ramp = new ushort[GammaNative.RampLength];
        for (var channel = 0; channel < 3; channel++)
            for (var i = 0; i < Steps; i++)
                ramp[channel * Steps + i] = (ushort)Math.Round(i / (double)Last * Full);
        return ramp;
    }

    /// <summary>
    /// True when a ramp read from a display looks like a real one (rising, and reaching most of full scale). A driver that returns
    /// zeros must not be taken as the picture to go back to.
    /// </summary>
    public static bool IsSane(ushort[] ramp)
    {
        if (ramp.Length != GammaNative.RampLength) return false;
        for (var channel = 0; channel < 3; channel++)
        {
            for (var i = 1; i < Steps; i++)
                if (ramp[channel * Steps + i] < ramp[channel * Steps + i - 1]) return false;
            if (ramp[channel * Steps + Last] < Full / 2) return false;
        }
        return true;
    }

    /// <summary>The ramp for <paramref name="color"/>, laid over <paramref name="original"/> so a calibration the display already has is kept.</summary>
    public static ushort[] Build(SoftwareColor color, ushort[] original)
    {
        color = color.Clamped();
        var contrast = color.Contrast / 100.0;
        var brightness = color.Brightness / 100.0;
        var tint = Tint(color.Temperature);

        var ramp = new ushort[GammaNative.RampLength];
        for (var i = 0; i < Steps; i++)
        {
            var level = Math.Clamp((i / (double)Last - 0.5) * contrast + 0.5, 0, 1) * brightness;
            for (var channel = 0; channel < 3; channel++)
                ramp[channel * Steps + i] = Lookup(original, channel, level * tint[channel]);
        }
        return ramp;
    }

    /// <summary>The original curve at a fractional position (0-1), by linear interpolation.</summary>
    private static ushort Lookup(ushort[] original, int channel, double position)
    {
        var at = Math.Clamp(position, 0, 1) * Last;
        var low = (int)Math.Floor(at);
        var high = Math.Min(low + 1, Last);
        var a = original[channel * Steps + low];
        var b = original[channel * Steps + high];
        return (ushort)Math.Round(a + (b - a) * (at - low));
    }

    /// <summary>How much of each channel (red, green, blue) a colour temperature lets through, relative to the neutral white point.</summary>
    public static double[] Tint(int kelvin)
    {
        var target = WhitePoint(kelvin);
        var neutral = WhitePoint(SoftwareColor.TemperatureNeutral);
        var share = new[] { target[0] / neutral[0], target[1] / neutral[1], target[2] / neutral[2] };
        var peak = share.Max();
        return share.Select(s => Math.Max(s / peak, LeastChannel)).ToArray();
    }

    /// <summary>Approximate colour of a black body at <paramref name="kelvin"/> as red, green, blue shares (0-1); the usual curve fit (Tanner Helland).</summary>
    private static double[] WhitePoint(int kelvin)
    {
        var t = kelvin / 100.0;
        var red = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        var green = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        var blue = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return new[] { Math.Clamp(red, 1, 255) / 255, Math.Clamp(green, 1, 255) / 255, Math.Clamp(blue, 1, 255) / 255 };
    }
}
