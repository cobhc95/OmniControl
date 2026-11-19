namespace OmniControl.App.Features.Osd;

/// The pictures a text pop-up can carry.
public enum OsdIcon { Play, Pause, Track, UsbConnected, UsbRemoved, Screenshot, Plug }

/// How a text pop-up colours its picture and second line: the usual look, a thing that is on (accent), off (muted) or failed (red).
public enum OsdTone { Normal, On, Off, Failed }

/// A pop-up that tells something (a device was plugged in, a track started) instead of showing a level. <see cref="Detail"/> is an optional
/// third, smaller muted line shortened in the middle when long (a saved file's folder). <see cref="LibraryIcon"/> (an icon value of the
/// icon library or a user icon, such as the one picked for a smart device) is drawn instead of <see cref="Icon"/> when it names something.
public sealed record OsdMessage(OsdIcon Icon, string Title, string? Subtitle, string? Detail = null, string? LibraryIcon = null, OsdTone Tone = OsdTone.Normal);
