namespace OmniControl.App.Services;

/// <summary>What the main window's X button does. Stored by number: do not reorder.</summary>
public enum CloseBehavior { Ask, MinimizeToTray, Exit }

/// <summary>The one list of close behaviours and their wording: the settings card and the "close" dialog both read it.</summary>
public static class CloseBehaviors
{
    public static readonly IReadOnlyList<(CloseBehavior Value, string Label)> All = new[]
    {
        (CloseBehavior.Ask, "Ask each time"),
        (CloseBehavior.MinimizeToTray, "Minimise to tray"),
        (CloseBehavior.Exit, "Quit"),
    };

    public static string LabelOf(CloseBehavior value) => All.First(e => e.Value == value).Label;

    public static CloseBehavior Find(string label) => All.FirstOrDefault(e => e.Label == label).Value;
}
