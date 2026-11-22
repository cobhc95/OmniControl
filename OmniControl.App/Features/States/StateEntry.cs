namespace OmniControl.App.Features.States;

/// What sort of value a state has: on or off, a level between a minimum and a maximum, or one of a list of choices.
public enum StateKind { OnOff, Level, Choice }

/// One reading of a state: a number (on/off as 1 or 0, a level) and/or a text (on/off as "on"/"off", a choice), and when the owner last
/// read or set it (null: the owner does not say). Two readings are the same value when number and text match; the time is not compared.
public sealed record StateValue(double? Number, string? Text, DateTime? At = null, string? Subject = null)
{
    public const string OnWord = "on", OffWord = "off";

    public static StateValue Of(bool on, DateTime? at = null, string? subject = null) => new(on ? 1 : 0, on ? OnWord : OffWord, at, subject);

    public static StateValue Of(double level, DateTime? at = null) => new(level, null, at);

    public static StateValue Of(string choice, DateTime? at = null) => new(null, choice, at);

    public bool SameValue(StateValue? other) => other is not null && Number == other.Number && Text == other.Text && Subject == other.Subject;
}

/// One state in the master list (<see cref="StateRegistry"/>): its stable id (stored in rules, never rename), its name, the page it
/// belongs to (<see cref="Group"/>, the page's Title) and an optional heading under it, its kind (with the level's range and unit, or the
/// choices), what is read now (<see cref="Read"/>: the owner's cached value, cheap, never a slow call, read on the UI thread), and one
/// short sentence saying how the value is known. The owner calls <see cref="StateRegistry.Notify"/> when it may have changed.
/// <see cref="Set"/>, when the owner gives one, changes the value the way its own control does (the tray flyout's sliders, tiles and
/// pickers use it); <see cref="Common"/> names the card of the basic set another PC sees (<see cref="CommonGroups"/>), or null; set by the owner. <see cref="ScreenPower"/> marks the on/off state of one screen's power (the Displays page, and Other PCs' Displays view, give it the screen's header switch instead of a card); <see cref="RemotePc"/> is the other PC the state is read from (the lists mark it "Other PCs · name"), null for this PC's own; <see cref="Icon"/> is the state's own icon (a library id or a user icon, <see cref="Icons.IconLibrary"/>; null: its page's icon); <see cref="Short"/> is a shorter name for tight places (the flyout's rows), the <see cref="Name"/> when not given.
public sealed record StateEntry(string Id, string Name, string Group, StateKind Kind, Func<StateValue?> Read, string HowKnown,
    string? Heading = null, int Min = 0, int Max = 100, string Unit = "", Func<IReadOnlyList<string>>? Choices = null, string Keywords = "",
    Action<StateValue>? Set = null, string? Short = null, string? Icon = null, IReadOnlyList<string>? SuggestedIcons = null, StateRecheck? Recheck = null,
    Func<StateLiveness>? Liveness = null, bool ScreenPower = false, string? Common = null, Shared.Services.RemotePcLabel? RemotePc = null, string? Compact = null, OnOffIcons? StateIcons = null,
    string? Note = null, StateMenu? Menu = null, StateMenuPart MenuPart = StateMenuPart.Top)
{
    // Menu: the side list a state picker shows this state in instead of its own list (one row "<Menu.Name> ▸" opens it), where the row reads ShortName with Note muted at its right; MenuPart: where in it.
    // Note: a muted fact a picker shows under the state's short name ("Laptop panel", "External monitor"); null = none.
    // Compact: the one or two words a half-width card or a state button uses ("Sound", "Mic"; the icon carries the rest, the tooltip the full name); null = the Name.
    // StateIcons: an on/off state's icon for each value (a state button shows the one of the value now: "mute" while muted, "volume" while not); null = none.

    /// What a tight place calls the state.
    public string ShortName => Short ?? Name;

    /// The icon it shows: its first suggested library icon (<see cref="SuggestedIcons"/>, the icon picker's "Suggested for this"), else its own <see cref="Icon"/>.
    public string? ShownIcon => Icons.IconLibrary.DefaultOf(SuggestedIcons, Icon);

    /// A reading as words ("on", "50 %", "Speakers"); "not known" when there is none.
    public string Words(StateValue? reading) => reading switch
    {
        null => "not known",
        { Text: { } text } => text,
        { Number: { } number } => $"{number:0.##}{(Unit.Length > 0 ? " " + Unit : string.Empty)}",
        _ => "not known",
    };
}

/// How a state's owner reads its real value again on request, in the background (the tray panel asks while it is open): whether it can now
/// (false: the value shown is only the last one known, and nothing cheap can read it; the panel shows it faded) and the read itself, whose
/// answer arrives as any change does (<see cref="StateRegistry.Notify"/>).
public sealed record StateRecheck(Func<bool> CanCheck, Func<Task> CheckAsync);

/// What a state's owner is doing with the value right now and how far the value can be trusted: it is
/// being changed (<see cref="Acting"/>) or read (<see cref="Reading"/>), the last change or read failed (<see cref="Failure"/>, one plain
/// sentence), or the value is only the last one known (<see cref="NotSure"/>, why). Cheap, read on the UI thread; the owner calls
/// <see cref="StateRegistry.NotifyLiveness"/> when it changes. A state without one is always sure and calm.
public sealed record StateLiveness(bool Acting, bool Reading, string? Failure, string? NotSure);

/// A side list of states in the state pickers (owner-approved for the network states, MAP "Exceptions"): the picker's row, the heading over
/// its first part and the hint of the find box over its rest.
public sealed record StateMenu(string Name, string Heading, string Find);

/// Where a state sits in its <see cref="StateMenu"/>: under the heading, after a thin line, or in the rest behind the find box (muted).
public enum StateMenuPart { Top, Middle, Rest }

/// The icons of an on/off state for each of its values (library ids).
public sealed record OnOffIcons(string On, string Off);

/// A state's value changed: the reading before (null: not known) and now, and the chain of rules whose run caused it, when known.
public sealed record StateChange(StateEntry Entry, StateValue? Before, StateValue? After, Shared.Contracts.RunChain? Chain);
