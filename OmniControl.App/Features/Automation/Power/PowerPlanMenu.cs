
namespace OmniControl.App.Features.Automation.Power;

/// The pop-down of "+ Add power plan": the plans Windows has that are not added yet, as the shared menu rows; the answer is the plan clicked, or null.
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
        var menu = new PointerMenu();
        menu.Closed += () => answer.TrySetResult(null); // closed without a pick: no plan (a pick has set its answer by now)
        menu.Show(anchor, rows, Title);
        return answer.Task;
    }
}
