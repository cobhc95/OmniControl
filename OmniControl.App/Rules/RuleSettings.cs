using OmniControl.App.Services;

namespace OmniControl.App.Rules;

/// <summary>
/// The custom gesture rules, stored as JSON in %APPDATA%\OmniControl\rules.json, with the commands the user allowed
/// (<see cref="CommandApproval"/>: a hash per allowed command; an edited command has another hash and is asked about again).
/// Loading never runs anything.
/// </summary>
public sealed class RuleSettings
{
    public List<Rule> Rules { get; set; } = new();

    /// <summary>Hashes of the command and program steps the user allowed in the confirm window.</summary>
    public HashSet<string> Approved { get; set; } = new();

    private const string FileName = "rules.json";

    public static RuleSettings Load() => AppDataStore.Load<RuleSettings>(FileName) ?? new();

    public void Save() => AppDataStore.Save(FileName, this);
}
