namespace OmniControl.App.Shared.Services;

/// A Windows audio device's name has its adapter in brackets ("Speakers (Realtek(R) Audio)"): the short name is the part before it,
/// the adapter the part inside.
public static class DeviceNames
{
    public static string Short(string deviceName) => Bracket(deviceName) is var at && at > 0 ? deviceName[..at] : deviceName;

    /// The adapter ("Realtek(R) Audio"); empty when the name has none.
    public static string Adapter(string deviceName) =>
        Bracket(deviceName) is var at && at > 0 && deviceName.EndsWith(')') ? deviceName[(at + 2)..^1] : string.Empty;

    private static int Bracket(string deviceName) => deviceName.IndexOf(" (", StringComparison.Ordinal);
}
