using OmniControl.App.Automation;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Power;

/// <summary>The power plans the user added as actions (power-plans.json): the plans' ids (Windows' own, so a plan renamed in Windows keeps its action).</summary>
public sealed class PowerPlanSettings
{
    public List<string> Added { get; set; } = new();
}

/// <summary>
/// Keeps one "Switch to ..." action in the master list for each Windows power plan the user added (the Power section's "Power plan" group;
/// "+ Add power plan" lists the plans not added yet, read live from Windows, and Remove takes one out, the way the Launch section adds and
/// removes programs). The plan Windows has active carries the tag "active"; a plan Windows no longer has stays listed, saying so. Each
/// action's settings hold the editor of that plan (<see cref="PowerPlanViewModel"/>).
/// </summary>
public sealed class PowerPlanActions
{
    public const string Group = "Power plan";
    private const string ActiveTag = "active";
    private static readonly DataStore Store = DataStores.PowerPlans;

    private readonly IPowerPlans _plans;
    private readonly HotkeyRegistry _hotkeys;
    private readonly PowerPlanSettings _settings = AppDataStore.Load<PowerPlanSettings>(Store) ?? new();
    private readonly Dictionary<Guid, (HotkeyAction Action, string Name, bool Active)> _registered = new();

    public PowerPlanActions(IPowerPlans plans, HotkeyRegistry hotkeys)
    {
        (_plans, _hotkeys) = (plans, hotkeys);
        hotkeys.RegisterPickerCommand(new HotkeyAction("power.plan.add", "+ Add power plan", AutomationService.Category, "add power plan windows", _ => { }, group: Group)
            { PickerSection = PowerActions.Power, Command = AddAsync, PickerOpened = Sync });
        Sync();
    }

    /// <summary>Takes power-plans.json from disk as it is now (an import wrote it): the actions of plans no longer listed are taken out and the rest are made again.</summary>
    public bool ReloadFromDisk()
    {
        SettingsCopy.Into(_settings, AppDataStore.Load<PowerPlanSettings>(Store) ?? new());
        foreach (var id in _registered.Keys.Where(id => !_settings.Added.Contains(id.ToString())).ToList())
            if (_registered.Remove(id, out var gone)) _hotkeys.Unregister(gone.Action.Id);
        Sync();
        return true;
    }

    /// <summary>Reads the plans from Windows and brings the registered actions (names, the active tag, what is missing) up to date.</summary>
    public void Sync()
    {
        var live = ReadPlans();
        foreach (var text in _settings.Added)
        {
            if (!Guid.TryParse(text, out var id)) continue;
            var plan = live?.FirstOrDefault(p => p.Id == id);
            var wanted = (Name: plan?.Name ?? PlanKnownAs(id), Active: plan?.IsActive == true);
            if (_registered.TryGetValue(id, out var have) && have.Name == wanted.Name && have.Active == wanted.Active) continue;
            if (have.Action is not null) _hotkeys.Unregister(have.Action.Id);
            var action = Make(id, wanted.Name, wanted.Active, missing: live is not null && plan is null);
            _hotkeys.Register(action);
            _registered[id] = (action, wanted.Name, wanted.Active);
        }
    }

    private IReadOnlyList<PowerPlan>? ReadPlans()
    {
        IReadOnlyList<PowerPlan>? plans = null;
        Guard.Run("Listing the power plans", () => plans = _plans.Plans());
        return plans;
    }

    private string PlanKnownAs(Guid id) => _registered.TryGetValue(id, out var known) ? known.Name : id.ToString();

    private HotkeyAction Make(Guid id, string name, bool active, bool missing) =>
        new($"power.plan.{id}", $"Switch to {name}", AutomationService.Category, $"power plan switch windows {name}", _ => Switch(id), group: Group)
        {
            PickerSection = PowerActions.Power,
            SuggestedIcons = new[] { "battery-charging" },
            Tag = active ? ActiveTag : null,
            Remove = () => Remove(id),
            RemoveText = "Remove power plan",
            ManageSettings = new[]
            {
                ActionOption.Panel("plan.editor", name, () => new PowerPlanViewModel(_plans, id)),
            },
            UnavailableReason = missing ? "Windows no longer has this power plan." : null,
        };

    private void Switch(Guid id)
    {
        if (Guard.Run("Switching the power plan", () => _plans.Activate(id)) is null) Sync();
    }

    private void Remove(Guid id)
    {
        _settings.Added.Remove(id.ToString());
        AppDataStore.Save(Store, _settings);
        if (_registered.Remove(id, out var gone)) _hotkeys.Unregister(gone.Action.Id);
    }

    private async Task<IReadOnlyList<HotkeyAction>> AddAsync(Avalonia.Visual from)
    {
        var free = (ReadPlans() ?? Array.Empty<PowerPlan>()).Where(p => !_settings.Added.Contains(p.Id.ToString())).ToList();
        if (await PowerPlanMenu.PickAsync(from, free) is not { } plan) return Array.Empty<HotkeyAction>();
        _settings.Added.Add(plan.Id.ToString());
        AppDataStore.Save(Store, _settings);
        Sync();
        return _registered.TryGetValue(plan.Id, out var made) ? new[] { made.Action } : Array.Empty<HotkeyAction>();
    }
}
