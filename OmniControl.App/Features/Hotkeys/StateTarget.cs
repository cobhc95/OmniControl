using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Hotkeys;

/// What the run path should do with an On or Off action whose thing has a known state.
public enum StateVerdict { Run, AlreadyThere, Unknown }

/// The one rule of "set to a state": an action whose family role is On or Off and which is linked to an on/off state does nothing when the
/// thing is already there (a simulation method that taps a toggle would otherwise flip it back).
public static class StateTarget
{
    public const string OnRole = "On", OffRole = "Off";

    /// The state a role asks for (true = on, false = off); null for any other role (Toggle, Up...).
    public static bool? Wanted(string? role) => role switch
    {
        OnRole => true,
        OffRole => false,
        _ => null,
    };

    public static StateVerdict Decide(string? role, StateValue? now)
    {
        if (Wanted(role) is not { } wanted) return StateVerdict.Run;
        if (now?.Number is not { } number) return StateVerdict.Unknown;
        return (number != 0) == wanted ? StateVerdict.AlreadyThere : StateVerdict.Run;
    }
}
