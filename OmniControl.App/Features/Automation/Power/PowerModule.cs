namespace OmniControl.App.Features.Automation.Power;

/// The power plan actions.
public sealed class PowerModule : IFeatureModule
{
    public void Register(FeatureContext context)
    {
        var plans = new PowerPlanActions(new PowerPlanService(), context.Actions);
        context.Imports.OnReload(DataStores.PowerPlans, plans.ReloadFromDisk);
    }
}
