namespace OmniControl.App.Osd;

/// <summary>The pictures a text pop-up can carry.</summary>
public enum OsdIcon { Play, Pause, Track, UsbConnected, UsbRemoved, Screenshot }

/// <summary>A pop-up that tells something (a device was plugged in, a track started) instead of showing a level.</summary>
public sealed record OsdMessage(OsdIcon Icon, string Title, string? Subtitle);
