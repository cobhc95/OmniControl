using Avalonia.Controls.Templates;

namespace OmniControl.App.Features.Automation.Power;

/// The power plan actions and their editor (shown in the action window's settings pane).
public sealed class PowerModule : IFeatureModule
{
    public IReadOnlyList<IDataTemplate> Templates { get; } = new[] { ViewTemplate.For<PowerPlanViewModel, PowerPlanEditor>() };

    public void Register(FeatureContext context)
    {
        var plans = new PowerPlanActions(new PowerPlanService(), context.Actions);
        context.Imports.OnReload(DataStores.PowerPlans, plans.ReloadFromDisk);
    }
}
