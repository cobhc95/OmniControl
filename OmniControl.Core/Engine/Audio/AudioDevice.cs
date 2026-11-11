namespace OmniControl.Core.Engine;

/// <summary>An active playback device.</summary>
public sealed record AudioDevice(string Id, string Name, bool IsDefault);
