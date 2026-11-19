using OmniControl.App.Features.States;

namespace OmniControl.App.Shared.Ui.Pages;

/// How a <see cref="GroupBadge"/> is coloured: muted, good (green) or caution (yellow).
public enum BadgeTone { Normal, Good, Caution }

/// A small muted fact at the right of a group's header: a library icon and words, with a tooltip for the detail.
public sealed record GroupBadge(string Icon, string Text, string? Tip, BadgeTone Tone)
{
    public bool IsGood => Tone == BadgeTone.Good;

    public bool IsCaution => Tone == BadgeTone.Caution;
}

/// Makes the Power header's battery fact from the shared battery states (charge and status).
public static class BatteryBadge
{
    private const double OnBattery = 0, Charging = 2;

    /// On battery at or above this charge the glyph shows a full battery, below it the low one (the library's two battery steps).
    private const double FullStep = 50;

    /// Null when the charge is not known (no battery); the status gives the icon and the tooltip when it is known.
    public static GroupBadge? Make(StateEntry? level, StateEntry? status)
    {
        if (level?.Read() is not { Number: { } percent } charge) return null;
        var reading = status?.Read();
        var low = reading?.Number == OnBattery && percent <= Timing.BatteryLowPercent;
        var icon = reading?.Number switch { Charging => "battery-charging", OnBattery or null => percent >= FullStep ? "battery-4" : "battery", _ => "plug-connected" };
        var tone = low ? BadgeTone.Caution : reading?.Number == Charging ? BadgeTone.Good : BadgeTone.Normal;
        return new GroupBadge(icon, level.Words(charge), reading?.Text, tone);
    }
}
