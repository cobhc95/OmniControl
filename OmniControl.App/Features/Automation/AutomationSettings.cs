namespace OmniControl.App.Features.Automation;

/// The automation rules, custom actions and saved scripts, stored as JSON in %APPDATA%\OmniControl\automation.json, with the hashes of the program steps the user allowed. Loading never runs anything.
public sealed class AutomationSettings
{
    public List<AutomationRule> Rules { get; set; } = new();
    public List<CustomAction> Customs { get; set; } = new();
    public List<Script> Scripts { get; set; } = new();

    /// Hashes of the "open a program" steps the user allowed in the confirm window (<see cref="Shared.Scripts.CommandApproval"/>).
    public HashSet<string> Approved { get; set; } = new();

    /// The programs the user added in the trigger picker ("Added by you"): each is a state, "&lt;Name&gt; is running". Paths belong to this PC.
    public List<AddedProgram> AddedPrograms { get; set; } = new();

    private static readonly DataStore Store = DataStores.Automation;

    public static AutomationSettings Load() => AppDataStore.Load<AutomationSettings>(Store) ?? new();

    public void Save() => AppDataStore.SaveSoon(Store, this);
}

/// A program's .exe and the name Windows gave it when the user added it.
public sealed record AddedProgram(string Path, string Name);
