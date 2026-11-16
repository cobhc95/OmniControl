namespace OmniControl.App.Services;

/// <summary>A Windows audio device's name has its adapter in brackets ("Speakers (Realtek(R) Audio)"): the short name is the part before it.</summary>
public static class DeviceNames
{
    public static string Short(string deviceName)
    {
        var at = deviceName.IndexOf(" (", StringComparison.Ordinal);
        return at > 0 ? deviceName[..at] : deviceName;
    }
}
