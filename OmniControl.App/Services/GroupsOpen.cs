namespace OmniControl.App.Services;

/// <summary>How the collapsible card groups of every page start when a page opens. Stored by number: do not reorder.</summary>
public enum GroupsOpenChoice { Remember, AlwaysOpen, AlwaysCollapsed }

/// <summary>
/// The one "Groups when a page opens" setting: its choices and wording (the sidebar foot's chip and the App settings card both read this list) and
/// the change notice every group listens to. readers ask <see cref="Current"/>; the setting itself lives in <see cref="AppSettings.GroupsOpen"/>.
/// </summary>
public static class GroupsOpen
{
    public static readonly IReadOnlyList<(GroupsOpenChoice Value, string Label)> All = new[]
    {
        (GroupsOpenChoice.AlwaysOpen, "Always open"),
        (GroupsOpenChoice.AlwaysCollapsed, "Always collapsed"),
        (GroupsOpenChoice.Remember, "Remember last state"),
    };

    /// <summary>The choice was changed (here or by Restore defaults); open groups and the chip follow.</summary>
    public static event Action? Changed;

    public static GroupsOpenChoice Current => AppSettings.Current.GroupsOpen;

    public static string LabelOf(GroupsOpenChoice value) => All.First(e => e.Value == value).Label;

    public static GroupsOpenChoice Find(string label) => All.FirstOrDefault(e => e.Label == label).Value;

    /// <summary>The chip's words: "Groups: Always open".</summary>
    public static string ChipText => "Groups: " + LabelOf(Current);

    public static void Set(GroupsOpenChoice value)
    {
        var settings = AppSettings.Current;
        if (settings.GroupsOpen == value) return;
        settings.GroupsOpen = value;
        settings.Save();
        Changed?.Invoke();
    }
}
