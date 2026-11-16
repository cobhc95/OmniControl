namespace OmniControl.App.Pages.Presets;

/// <summary>A section of the preset manager's left pane: one of the app's sidebar pages, in the sidebar's order, with its icon.</summary>
public sealed record PresetSection(string Title, string Icon);

/// <summary>One entry of the left pane: the page and how many preset groups it has. A page without any is dimmed ("none") and cannot be chosen.</summary>
public sealed class PresetSectionItem
{
    public PresetSectionItem(PresetSection section, int groupCount)
    {
        Title = section.Title;
        Icon = section.Icon;
        GroupCount = groupCount;
    }

    public string Title { get; }
    public string Icon { get; }
    public int GroupCount { get; }

    public bool HasPresets => GroupCount > 0;

    public string CountText => GroupCount switch
    {
        0 => "none",
        1 => "1 group",
        _ => $"{GroupCount} groups",
    };
}
