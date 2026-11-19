namespace OmniControl.App.Shell;

/// A sidebar entry that opens a window instead of showing a page (Actions and hotkeys), pinned at the sidebar's bottom.
public sealed record NavLauncher(string Title, string Icon, Action Open);
