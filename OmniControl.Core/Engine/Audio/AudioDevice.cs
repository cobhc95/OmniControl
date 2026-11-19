namespace OmniControl.Core.Engine;

/// An active playback device.
public sealed record AudioDevice(string Id, string Name, bool IsDefault);
