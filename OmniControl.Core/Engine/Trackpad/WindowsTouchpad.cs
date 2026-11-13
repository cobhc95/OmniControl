using Microsoft.Win32;

namespace OmniControl.Core.Engine.Trackpad;

/// <summary>
/// What Windows' own touchpad settings do (Settings > Bluetooth &amp; devices > Touchpad), read from the registry and never written:
/// OmniControl only says when Windows also acts on a gesture, the user changes it in Windows. A value that is missing is Windows'
/// default, which is on. For the swipe values any number but 0 means Windows runs one of its swipe actions (0 = "Nothing").
/// </summary>
public sealed record WindowsTouchpad(bool TouchpadOn, bool ThreeFingerSwipes, bool FourFingerSwipes, bool PinchToZoom)
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
    private const string StatusKey = Key + @"\Status";

    /// <summary>The Settings page where the user turns Windows' gestures off.</summary>
    public const string SettingsPage = "ms-settings:devices-touchpad";

    /// <summary>True when Windows itself also does something with this gesture.</summary>
    public bool Uses(WindowsGesture gesture) => gesture switch
    {
        WindowsGesture.ThreeFingerSwipes => ThreeFingerSwipes,
        WindowsGesture.FourFingerSwipes => FourFingerSwipes,
        _ => PinchToZoom,
    };

    /// <summary>Reads the current user's settings (a few registry values; cheap enough for the UI thread).</summary>
    public static WindowsTouchpad Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        using var status = Registry.CurrentUser.OpenSubKey(StatusKey);
        return new WindowsTouchpad(
            IsOn(status, "Enabled"),
            IsOn(key, "ThreeFingerSlideEnabled"),
            IsOn(key, "FourFingerSlideEnabled"),
            IsOn(key, "ZoomEnabled"));
    }

    private static bool IsOn(RegistryKey? key, string name) => key?.GetValue(name) is not int value || value != 0;
}
