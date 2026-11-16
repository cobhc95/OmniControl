namespace OmniControl.App.Shell;

/// <summary>A sidebar entry that opens a window instead of showing a page (Actions and hotkeys), pinned at the sidebar's bottom.</summary>
public sealed record NavLauncher(string Title, string Icon, Action Open);
