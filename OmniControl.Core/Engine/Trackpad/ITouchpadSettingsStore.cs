namespace OmniControl.Core.Engine.Trackpad;

/// <summary>
/// Where Windows keeps its touchpad settings (<see cref="WindowsGestureSettings"/>): the real one is <see cref="TouchpadSettingsStore"/>,
/// tests use a fake so nothing on the PC is changed. A raw value of null means the registry value is missing (Windows' default applies);
/// writing null deletes it again. A stored value and the value Windows acts on can differ: the shell keeps its own copy of the three- and
/// four-finger settings and refreshes it only when told (<see cref="Announce"/>).
/// </summary>
public interface ITouchpadSettingsStore
{
    /// <summary>Reads one setting's stored raw value. Returns null when it was read, or one plain sentence saying why not.</summary>
    string? Read(WindowsGestureSetting setting, out int? value);

    /// <summary>Stores one setting's raw value. Returns null when Windows took it, or one plain sentence saying why not.</summary>
    string? Write(WindowsGestureSetting setting, int? value);

    /// <summary>
    /// Makes Windows act on the stored value now (tells the shell to refresh its copy; nothing to do for a setting Windows' input applies
    /// as it is written). Returns null when done, or one plain sentence saying why not.
    /// </summary>
    string? Announce(WindowsGestureSetting setting);

    /// <summary>
    /// The value Windows acts on now: the shell's own copy for a setting it keeps one of, else the stored value. Returns null when it was
    /// read, or one plain sentence saying why not.
    /// </summary>
    string? ReadInEffect(WindowsGestureSetting setting, out int? value);
}
