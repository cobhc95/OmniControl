namespace OmniControl.App.Hotkeys;

/// <summary>What a mouse button, gesture or rule chose for a program-starting action itself (empty: the action's own).</summary>
public sealed record LaunchSettings(string Path, string Arguments, string StartIn);
