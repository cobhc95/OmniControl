namespace OmniControl.App.Features.Hotkeys;

/// What a mouse button, gesture or rule chose for a program-starting action itself (empty: the action's own).
public sealed record LaunchSettings(string Path, string Arguments, string StartIn);
