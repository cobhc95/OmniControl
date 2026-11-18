using OmniControl.App.States;

namespace OmniControl.App.Hotkeys;

/// <summary>What the run path should do with an On or Off action whose thing has a known state.</summary>
public enum StateVerdict { Run, AlreadyThere, Unknown }

/// <summary>What <see cref="HotkeyRegistry"/> asks for the state an action is linked to (<see cref="HotkeyAction.StateId"/>); <see cref="States.StateRegistry"/> answers.</summary>
public interface IStateReader
{
    /// <summary>The state's value now, from its owner's cache (null: not known, or no such state).</summary>
    StateValue? Read(string id);

    /// <summary>Asks the owner to read the real value again, then calls <paramref name="done"/> on the UI thread. False (and <paramref name="done"/> is never called) when the owner cannot.</summary>
    bool Refresh(string id, Action done);
}

/// <summary>
/// The one rule of "set to a state": an action whose family role is On or Off and which is linked to an on/off state does nothing when the
/// thing is already there (a simulation method that taps a toggle would otherwise flip it back).
/// </summary>
public static class StateTarget
{
    public const string OnRole = "On", OffRole = "Off";

    /// <summary>The state a role asks for (true = on, false = off); null for any other role (Toggle, Up...).</summary>
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
