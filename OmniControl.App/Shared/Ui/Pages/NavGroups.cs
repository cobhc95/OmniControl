namespace OmniControl.App.Shared.Ui.Pages;

/// The sidebar's group headings, defined once. A page names its group in <see cref="PageViewModel.NavGroup"/>; the sidebar shows a
/// heading above the first visible page of each group, in the order the pages are registered in the shell.
public static class NavGroups
{
    public const string Devices = "DEVICES";
    public const string System = "SYSTEM";
}

/// A group heading in the sidebar list: not a page, never selectable.
public sealed record NavHeading(string Text);
