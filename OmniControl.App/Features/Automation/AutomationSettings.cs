namespace OmniControl.App.Features.Automation;

/// The automation rules, custom actions and saved scripts, stored as JSON in %APPDATA%\OmniControl\automation.json, with the hashes of the program steps the user allowed. Loading never runs anything.
public sealed class AutomationSettings
{
    public List<AutomationRule> Rules { get; set; } = new();
    public List<CustomAction> Customs { get; set; } = new();
    public List<Script> Scripts { get; set; } = new();

    /// Hashes of the "open a program" steps the user allowed in the confirm window (<see cref="Shared.Scripts.CommandApproval"/>).
    public HashSet<string> Approved { get; set; } = new();

    private static readonly DataStore Store = DataStores.Automation;

    public static AutomationSettings Load() => AppDataStore.Load<AutomationSettings>(Store) ?? new();

    public void Save() => AppDataStore.Save(Store, this);
}
