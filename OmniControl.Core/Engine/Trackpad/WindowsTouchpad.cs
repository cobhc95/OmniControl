namespace OmniControl.Core.Engine.Trackpad;

/// What Windows' own touchpad settings do now (Settings > Bluetooth &amp; devices > Touchpad): whether the touchpad is on, and which of
/// the gestures in <see cref="WindowsGestureSettings"/> Windows runs itself. Only read here; <see cref="GestureTakeover"/> is the one
/// place that changes them.
public sealed record WindowsTouchpad(bool TouchpadOn, IReadOnlySet<WindowsGesture> InUse)
{
    internal const string Key = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
    private const string StatusKey = Key + @"\Status";

    /// The Settings page where the user changes Windows' gestures.
    public const string SettingsPage = "ms-settings:devices-touchpad";

    /// True when Windows itself also does something with this gesture.
    public bool Uses(WindowsGesture gesture) => InUse.Contains(gesture);

    /// Reads the current user's settings (a few registry values and one SystemParametersInfo; cheap enough for the UI thread).
    public static WindowsTouchpad Read(ITouchpadSettingsStore store)
    {
        using var status = Registry.CurrentUser.OpenSubKey(StatusKey);
        var inUse = new HashSet<WindowsGesture>();
        foreach (var setting in WindowsGestureSettings.All)
        {
            // A setting Windows won't report counts as in use: saying "Windows also does this" is the safe side.
            if (store.Read(setting, out var value) is not null || setting.IsOn(value)) inUse.Add(setting.Gesture);
        }
        return new WindowsTouchpad(status?.GetValue("Enabled") is not int enabled || enabled != 0, inUse);
    }
}
