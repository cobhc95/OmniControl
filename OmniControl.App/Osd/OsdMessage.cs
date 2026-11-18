namespace OmniControl.App.Osd;

/// <summary>The pictures a text pop-up can carry.</summary>
public enum OsdIcon { Play, Pause, Track, UsbConnected, UsbRemoved, Screenshot }

/// <summary>
/// A pop-up that tells something (a device was plugged in, a track started) instead of showing a level. <see cref="Detail"/> is an optional
/// third, smaller muted line shortened in the middle when long (a saved file's folder).
/// </summary>
public sealed record OsdMessage(OsdIcon Icon, string Title, string? Subtitle, string? Detail = null);
