using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.Core.Engine;

namespace OmniControl.App.Power;

/// <summary>The pop-down of "+ Add power plan": the plans Windows has that are not added yet, as the shared menu rows; the answer is the plan clicked, or null.</summary>
public static class PowerPlanMenu
{
    private const string Title = "Add a power plan";
    private const string NoneLeft = "Every plan is added.";

    public static Task<PowerPlan?> PickAsync(Avalonia.Visual from, IReadOnlyList<PowerPlan> plans)
    {
        var answer = new TaskCompletionSource<PowerPlan?>();
        if (from is not Control anchor)
        {
            answer.SetResult(null);
            return answer.Task;
        }
        var rows = plans.Count == 0
            ? new[] { MenuRowItem.Header(NoneLeft) }
            : plans.Select(p => new MenuRowItem(p.Name, string.Empty, false, string.Empty, new RelayCommand(() => answer.TrySetResult(p)))).ToArray();
        var list = new MenuRowList { Title = Title, ItemsSource = rows };
        var menu = new ListMenuFlyout { Placement = PlacementMode.Pointer, Content = list };
        list.Picked += (_, _) => menu.Hide();
        menu.Closed += (_, _) => answer.TrySetResult(null); // closed without a pick: no plan (a pick has set its answer by now)
        menu.ShowAt(anchor, showAtPointer: true);
        return answer.Task;
    }
}
