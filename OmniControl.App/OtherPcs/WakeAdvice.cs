using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.OtherPcs;

/// <summary>What the header of a PC says about waking it, and which part of it is shown (docs/mockups/other-pc-wake.html).</summary>
public enum WakeState
{
    /// <summary>Nothing to say (it never told this PC how it can be woken, and it is on).</summary>
    None,

    /// <summary>On, and everything a wake needs is on: the green tag.</summary>
    CanWake,

    /// <summary>On, its card can wake it but a setting is off: the yellow tag and "Turn on".</summary>
    SettingOff,

    /// <summary>Off and can be woken: the Wake pill.</summary>
    Wakeable,

    /// <summary>Off or on, and cannot be woken: the plain tag, the reason in its tooltip.</summary>
    Cannot,
}

/// <summary>The answer: the state and, for the tags, one plain reason.</summary>
public sealed record WakeAdvice(WakeState State, string Reason);

/// <summary>
/// Decides what can honestly be said about waking a PC from what it reported (<see cref="WakeFacts"/>), how it went away (the notice it sent)
/// and whether any PC on its network can send the signal. Pure: no network, no Windows.
/// </summary>
public static class WakeAdvices
{
    public const string Sleep = "sleep", Hibernate = "hibernate", Shutdown = "shutdown";

    /// <summary>True for the words a "going away" notice may carry.</summary>
    public static bool IsAway(string? word) => word is Sleep or Hibernate or Shutdown;

    public static WakeAdvice Of(WakeFacts? facts, bool online, string? away, bool pathExists, string name)
    {
        if (facts is null) return online ? new WakeAdvice(WakeState.None, string.Empty) : new WakeAdvice(WakeState.Cannot, $"{name} has not told this PC how it can be woken. Connect it once.");
        if (!facts.Known) return new WakeAdvice(WakeState.Cannot, $"{name} could not read its network card, so it cannot say whether it can be woken.");
        if (!facts.CardCanWake) return new WakeAdvice(WakeState.Cannot, $"The network card of {name} ({facts.Adapter}) cannot wake the PC.");
        if (online) return new WakeAdvice(facts.Ready ? WakeState.CanWake : WakeState.SettingOff, facts.Ready ? string.Empty : $"Wake is off on {name}.");
        if (!facts.Ready) return new WakeAdvice(WakeState.Cannot, $"Wake was not turned on in {name} while it was on.");
        if (away == Shutdown && facts.FastStartupOn) return new WakeAdvice(WakeState.Cannot, $"{name} was shut down with Fast Startup on, so its network card is off.");
        if (away is null && facts.FastStartupOn) return new WakeAdvice(WakeState.Cannot, $"It is not known how {name} went off, and with Fast Startup on a shut down cannot be woken.");
        if (!pathExists) return new WakeAdvice(WakeState.Cannot, $"No PC on the network of {name} is on to send the wake signal.");
        return new WakeAdvice(WakeState.Wakeable, string.Empty);
    }

    /// <summary>The words of the status tag of an offline PC that sent a notice: "Asleep since 09:12" (a date first when it was not today).</summary>
    public static string AwayWords(string away, DateTime at)
    {
        var when = at.Date == DateTime.Today ? $"{at:HH:mm}" : $"{at:d MMM HH:mm}";
        return away switch { Sleep => $"Asleep since {when}", Hibernate => $"Hibernating since {when}", _ => $"Shut down since {when}" };
    }
}
