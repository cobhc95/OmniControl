namespace OmniControl.App.Shared.Ui.Controls;

/// The rows of a preset menu (the preset picker's, the Easy view's Sound chip): one per preset, the accent bar at the current one, then the
/// owner's row at the foot under a thin line.
public static class PresetMenu
{
    public static IReadOnlyList<MenuRowItem> Rows(IEnumerable<string> names, string current, Action<string> pick, MenuRowItem foot)
    {
        var rows = names.Select(n => new MenuRowItem(n, string.Empty, n == current, string.Empty, new RelayCommand(() => pick(n)))).ToList();
        rows.Add(foot with { RuleAbove = true });
        return rows;
    }
}
