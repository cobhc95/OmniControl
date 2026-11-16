namespace OmniControl.App.States;

/// <summary>What sort of value a state has: on or off, a level between a minimum and a maximum, or one of a list of choices.</summary>
public enum StateKind { OnOff, Level, Choice }

/// <summary>
/// One reading of a state: a number (on/off as 1 or 0, a level) and/or a text (on/off as "on"/"off", a choice), and when the owner last
/// read or set it (null: the owner does not say). Two readings are the same value when number and text match; the time is not compared.
/// </summary>
public sealed record StateValue(double? Number, string? Text, DateTime? At = null)
{
    public const string OnWord = "on", OffWord = "off";

    public static StateValue Of(bool on, DateTime? at = null) => new(on ? 1 : 0, on ? OnWord : OffWord, at);

    public static StateValue Of(double level, DateTime? at = null) => new(level, null, at);

    public static StateValue Of(string choice, DateTime? at = null) => new(null, choice, at);

    public bool SameValue(StateValue? other) => other is not null && Number == other.Number && Text == other.Text;
}

/// <summary>
/// One state in the master list (<see cref="StateRegistry"/>): its stable id (stored in rules, never rename), its name, the page it
/// belongs to (<see cref="Group"/>, the page's Title) and an optional heading under it, its kind (with the level's range and unit, or the
/// choices), what is read now (<see cref="Read"/>: the owner's cached value, cheap, never a slow call, read on the UI thread), and one
/// short sentence saying how the value is known. The owner calls <see cref="StateRegistry.Notify"/> when it may have changed.
/// <see cref="Set"/>, when the owner gives one, changes the value the way its own control does (the tray flyout's sliders, tiles and
/// pickers use it); <see cref="Icon"/> is the state's own icon (a library id or a user icon, <see cref="Icons.IconLibrary"/>; null: its page's icon); <see cref="Short"/> is a shorter name for tight places (the flyout's rows), the <see cref="Name"/> when not given.
/// </summary>
public sealed record StateEntry(string Id, string Name, string Group, StateKind Kind, Func<StateValue?> Read, string HowKnown,
    string? Heading = null, int Min = 0, int Max = 100, string Unit = "", Func<IReadOnlyList<string>>? Choices = null, string Keywords = "",
    Action<StateValue>? Set = null, string? Short = null, string? Icon = null, IReadOnlyList<string>? SuggestedIcons = null, StateRecheck? Recheck = null)
{
    /// <summary>What a tight place calls the state.</summary>
    public string ShortName => Short ?? Name;

    /// <summary>The icon it shows: its first suggested library icon (<see cref="SuggestedIcons"/>, the icon picker's "Suggested for this"), else its own <see cref="Icon"/>.</summary>
    public string? ShownIcon => Icons.IconLibrary.DefaultOf(SuggestedIcons, Icon);

    /// <summary>A reading as words ("on", "50 %", "Speakers"); "not known" when there is none.</summary>
    public string Words(StateValue? reading) => reading switch
    {
        null => "not known",
        { Text: { } text } => text,
        { Number: { } number } => $"{number:0.##}{(Unit.Length > 0 ? " " + Unit : string.Empty)}",
        _ => "not known",
    };
}

/// <summary>
/// How a state's owner reads its real value again on request, in the background (the tray panel asks while it is open): whether it can now
/// (false: the value shown is only the last one known, and nothing cheap can read it; the panel shows it faded) and the read itself, whose
/// answer arrives as any change does (<see cref="StateRegistry.Notify"/>).
/// </summary>
public sealed record StateRecheck(Func<bool> CanCheck, Func<Task> CheckAsync);

/// <summary>A state's value changed: the reading before (null: not known) and now, and the chain of rules whose run caused it, when known.</summary>
public sealed record StateChange(StateEntry Entry, StateValue? Before, StateValue? After, Hotkeys.RunChain? Chain);
