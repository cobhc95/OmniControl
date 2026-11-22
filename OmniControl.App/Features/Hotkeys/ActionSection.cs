namespace OmniControl.App.Features.Hotkeys;

/// One section of the Choose-the-action window: its name, the icon of its pane-1 entry (null: none) and its place in the list (1 = first).
public sealed record ActionSection(string Name, string? Icon, int Rank);

/// The sections that are not sidebar pages. The pages' own sections (name, icon, order) are read from the live page list and handed to
/// <see cref="HotkeyRegistry.UseSections"/>; these come after them, in this order, and are what an entry names as its
/// <see cref="HotkeyAction.PickerSection"/>.
public static class ActionSections
{
    public const string Keys = "Keys and windows", Power = "Power", Launch = "Launch", Screenshot = "Screenshot", CustomActions = "Custom actions";

    /// The camera of the Screenshot section, also the picture of the screenshot OSD note.
    public const string ScreenshotIcon = "M3,7 H7.5 L9.5,4.5 H14.5 L16.5,7 H21 V19 H3 Z M12,8.5 A4.5,4.5 0 1 0 12,17.5 A4.5,4.5 0 1 0 12,8.5 Z M12,10.5 A2.5,2.5 0 1 1 12,15.5 A2.5,2.5 0 1 1 12,10.5 Z";

    /// The titles of the pages that also name a section the built-in actions are listed under (defined here once; the pages' own title is this).
    public const string Mouse = PageTitles.Mouse, Automation = PageTitles.Automation;
    private const int FirstExtraRank = 1000; // after any number of pages

    public static IReadOnlyList<ActionSection> Extras { get; } = new ActionSection[]
    {
        new(Keys, "M4,6 H20 A1,1 0 0 1 21,7 V17 A1,1 0 0 1 20,18 H4 A1,1 0 0 1 3,17 V7 A1,1 0 0 1 4,6 Z M5,8 V16 H19 V8 Z M7,9.5 H9 V11 H7 Z M10,9.5 H12 V11 H10 Z M13,9.5 H15 V11 H13 Z M7,13 H17 V14.5 H7 Z", FirstExtraRank),
        new(Power, "M11,3 H13 V12 H11 Z M7.1,5.6 L8.5,7 A6,6 0 1 0 15.5,7 L16.9,5.6 A8,8 0 1 1 7.1,5.6 Z", FirstExtraRank + 1),
        new(Launch, "M5,3 L19,12 L5,21 Z M7.5,7.5 V16.5 L14.5,12 Z", FirstExtraRank + 2),
        new(Screenshot, ScreenshotIcon, FirstExtraRank + 3),
        new(CustomActions, "M4,4 H11 V11 H4 Z M13,4 H20 V11 H13 Z M4,13 H11 V20 H4 Z M15.5,13 H17.5 V15.5 H20 V17.5 H17.5 V20 H15.5 V17.5 H13 V15.5 H15.5 Z", FirstExtraRank + 4),
    };

    /// The extra section with this name, or null for a page's section.
    public static ActionSection? Find(string name) => Extras.FirstOrDefault(s => s.Name == name);
}
