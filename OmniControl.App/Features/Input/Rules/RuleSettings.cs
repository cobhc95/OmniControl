namespace OmniControl.App.Features.Input.Rules;

/// The custom gesture rules, stored as JSON in %APPDATA%\OmniControl\rules.json, with the commands the user allowed
/// (<see cref="CommandApproval"/>: a hash per allowed command; an edited command has another hash and is asked about again).
/// Loading never runs anything.
public sealed class RuleSettings
{
    public List<Rule> Rules { get; set; } = new();

    /// Hashes of the command and program steps the user allowed in the confirm window.
    public HashSet<string> Approved { get; set; } = new();

    private static readonly DataStore Store = DataStores.GestureRules;

    public static RuleSettings Load() => AppDataStore.Load<RuleSettings>(Store) ?? new();

    public void Save() => AppDataStore.Save(Store, this);
}
