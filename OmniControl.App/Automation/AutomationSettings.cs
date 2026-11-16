using OmniControl.App.Services;

namespace OmniControl.App.Automation;

/// <summary>The automation rules, custom actions and saved scripts, stored as JSON in %APPDATA%\OmniControl\automation.json, with the hashes of the program steps the user allowed. Loading never runs anything.</summary>
public sealed class AutomationSettings
{
    public List<AutomationRule> Rules { get; set; } = new();
    public List<CustomAction> Customs { get; set; } = new();
    public List<Script> Scripts { get; set; } = new();

    /// <summary>Hashes of the "open a program" steps the user allowed in the confirm window (<see cref="Rules.CommandApproval"/>).</summary>
    public HashSet<string> Approved { get; set; } = new();

    private static readonly DataStore Store = DataStores.Automation;

    public static AutomationSettings Load() => AppDataStore.Load<AutomationSettings>(Store) ?? new();

    public void Save() => AppDataStore.Save(Store, this);
}
