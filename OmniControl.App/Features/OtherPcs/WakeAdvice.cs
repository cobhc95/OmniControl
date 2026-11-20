using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Features.OtherPcs;

/// What the header of a PC says about waking it, and which part of it is shown.
public enum WakeState
{
    /// Nothing to say (it never told this PC how it can be woken, and it is on).
    None,

    /// On, and everything a wake needs is on: the green tag.
    CanWake,

    /// On, its card can wake it but a setting is off: the yellow tag and the set-up cogwheel.
    SettingOff,

    /// Off and can be woken: the Wake pill.
    Wakeable,

    /// The card's wake settings could not be read (not known either way): a plain "Wake not confirmed" tag while on; while off the Wake pill is still offered, with a caution mark.
    Unconfirmed,

    /// Off or on, and cannot be woken: the plain tag, the reason in its tooltip.
    Cannot,
}

/// The answer: the state and, for the tags, one plain reason.
public sealed record WakeAdvice(WakeState State, string Reason);

/// Decides what can honestly be said about waking a PC from what it reported (<see cref="WakeFacts"/>), how it went away (the notice it sent)
/// and whether any PC on its network can send the signal. Pure: no network, no Windows.
public static class WakeAdvices
{
    public const string Sleep = "sleep", Hibernate = "hibernate", Shutdown = "shutdown";

    /// True for the words a "going away" notice may carry.
    public static bool IsAway(string? word) => word is Sleep or Hibernate or Shutdown;

    public static WakeAdvice Of(WakeFacts? facts, bool online, string? away, bool pathExists, string name)
    {
        if (facts is null) return online ? new WakeAdvice(WakeState.None, string.Empty) : new WakeAdvice(WakeState.Cannot, $"{name} has not told this PC how it can be woken. Connect it once.");
        if (!facts.Known)
            return online || pathExists
                ? new WakeAdvice(WakeState.Unconfirmed, $"{name} could not confirm whether its network card can wake it.")
                : new WakeAdvice(WakeState.Cannot, $"No PC on the network of {name} is on to send the wake signal.");
        if (!facts.CardCanWake) return new WakeAdvice(WakeState.Cannot, $"The network card of {name} ({facts.Adapter}) cannot wake the PC.");
        if (online) return new WakeAdvice(facts.Ready ? WakeState.CanWake : WakeState.SettingOff, facts.Ready ? string.Empty : $"Wake is off on {name}.");
        if (!facts.Ready) return new WakeAdvice(WakeState.Cannot, $"Wake was not turned on in {name} while it was on.");
        if (away == Shutdown && facts.FastStartupOn) return new WakeAdvice(WakeState.Cannot, $"{name} was shut down with Fast Startup on, so its network card is off.");
        if (away is null && facts.FastStartupOn) return new WakeAdvice(WakeState.Cannot, $"It is not known how {name} went off, and with Fast Startup on a shut down cannot be woken.");
        if (!pathExists) return new WakeAdvice(WakeState.Cannot, $"No PC on the network of {name} is on to send the wake signal.");
        return new WakeAdvice(WakeState.Wakeable, string.Empty);
    }

    /// One word for how a PC went away after its power countdown: "Asleep", "Off", or "Offline" when it sent no notice.
    public static string AwayWord(string? away) => away switch { Sleep or Hibernate => "Asleep", Shutdown => "Off", _ => "Offline" };

    /// The words of the status tag of an offline PC that sent a notice: "Asleep since 09:12" (a date first when it was not today).
    public static string AwayWords(string away, DateTime at)
    {
        var when = at.Date == DateTime.Today ? $"{at:HH:mm}" : $"{at:d MMM HH:mm}";
        return away switch { Sleep => $"Asleep since {when}", Hibernate => $"Hibernating since {when}", _ => $"Shut down since {when}" };
    }
}
