using System.Globalization;
using System.Text.RegularExpressions;

namespace OmniControl.App.Features.Automation.Power;

/// How one power plan setting reads, steps and is typed, from what Windows says about it (its units, limits and choices): the one place the
/// editor's cells ask. Times (units "Seconds" or "Minutes", the words Windows itself uses) read as "Never", "30 s", "10 min", "3 h"; a
/// time steps by one minute (an hour from an hour up, ten seconds under a minute) and another number by its Windows increment, or a hundredth of a small range.
public static partial class PowerValueFormat
{
    private const long NeverFrom = int.MaxValue; // Windows stores "never" as 0, and some plans as the largest signed value
    private const uint WholeRangeLimit = 10_000;

    [GeneratedRegex(@"^(\d+(?:[.,]\d+)?)\s*(s|sec|secs|second|seconds|m|min|mins|minute|minutes|h|hr|hrs|hour|hours)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeText();

    [GeneratedRegex(@"^(\d+)")]
    private static partial Regex LeadingNumber();

    private static bool IsTime(PowerSetting s) => s.Units is "Seconds" or "Minutes";
    private static long PerUnit(PowerSetting s) => s.Units == "Minutes" ? 60 : 1;

    public static string Show(PowerSetting setting, uint value)
    {
        if (setting.IsChoice) return setting.Choices.FirstOrDefault(c => c.Index == value)?.Name ?? value.ToString(CultureInfo.CurrentCulture);
        if (!IsTime(setting)) return value + Suffix(setting);
        var seconds = value * PerUnit(setting);
        if (setting.Minimum == 0 && (seconds == 0 || seconds >= NeverFrom)) return "Never";
        if (seconds >= 3600 && seconds % 3600 == 0) return $"{seconds / 3600} h";
        return seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds} s";
    }

    /// The value one step from <paramref name="value"/> (a choice wraps round), held within the limits.
    public static uint Next(PowerSetting setting, uint value, int direction)
    {
        if (setting.IsChoice)
        {
            var at = Math.Max(0, setting.Choices.ToList().FindIndex(c => c.Index == value));
            return setting.Choices[Shared.Services.Wrap.Index(at + direction, setting.Choices.Count)].Index;
        }
        if (!IsTime(setting)) return Clamp(setting, value + direction * (long)StepOf(setting));
        var seconds = value * PerUnit(setting);
        if (seconds >= NeverFrom) seconds = 0;
        var step = direction > 0 ? (seconds >= 3600 ? 3600 : seconds >= 60 ? 60 : 10) : (seconds > 3600 ? 3600 : seconds > 60 ? 60 : 10);
        return Clamp(setting, (seconds + direction * step) / PerUnit(setting));
    }

    /// What a typed text means, or false when it is not a number of this setting. A bare number of a time is minutes.
    public static bool TryParse(PowerSetting setting, string? typed, out uint value)
    {
        value = 0;
        var text = typed?.Trim() ?? string.Empty;
        if (setting.IsChoice) return false;
        if (!IsTime(setting))
        {
            if (LeadingNumber().Match(text) is not { Success: true } number || !long.TryParse(number.Value, out var plain)) return false;
            value = Clamp(setting, plain);
            return true;
        }
        if (text.Equals("never", StringComparison.OrdinalIgnoreCase)) text = "0";
        if (TimeText().Match(text) is not { Success: true } m
            || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) return false;
        var perWord = m.Groups[2].Value.ToLowerInvariant() switch
        {
            "" or "m" or "min" or "mins" or "minute" or "minutes" => 60,
            "h" or "hr" or "hrs" or "hour" or "hours" => 3600,
            _ => 1,
        };
        value = Clamp(setting, (long)Math.Round(amount * perWord / PerUnit(setting)));
        return true;
    }

    /// What to offer in the box when a number is opened for typing.
    public static string ForTyping(PowerSetting setting, uint value)
    {
        var shown = Show(setting, value);
        return shown == "Never" ? "0" : shown.Replace(" ", string.Empty);
    }

    private static uint StepOf(PowerSetting s)
    {
        var range = s.Maximum - s.Minimum;
        return range <= WholeRangeLimit ? Math.Max(s.Increment, (uint)Math.Ceiling(range * 0.01 / s.Increment) * s.Increment) : s.Increment;
    }

    private static uint Clamp(PowerSetting s, long value) => (uint)Math.Clamp(value, s.Minimum, s.Maximum);

    private static string Suffix(PowerSetting s) => s.Units switch
    {
        "" => string.Empty,
        "Percentage" or "Percent" => " %", // Windows' own words for a percentage
        var other => " " + other.ToLowerInvariant(),
    };
}
