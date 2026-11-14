namespace OmniControl.Core.Engine.Trackpad;

/// <summary>How Windows takes a change to one of its touchpad settings.</summary>
public enum SettingRoute
{
    /// <summary>
    /// A value under <c>HKCU\...\PrecisionTouchPad</c>, the same value the Settings app writes, acted on by the shell's touchpad gesture
    /// handler (<c>twinui.dll</c>) from its own copy, which it refreshes only when told (<see cref="ShellTouchpadSettings"/>, the setting's
    /// <see cref="WindowsGestureSetting.ShellId"/>). Not part of SystemParametersInfo.
    /// </summary>
    Registry,

    /// <summary>A user field of TOUCHPAD_PARAMETERS, changed with SPI_SETTOUCHPADPARAMETERS (Windows 11 build 26027 and later), saved and announced.</summary>
    TouchpadParameters,
}

/// <summary>
/// One of Windows' own touchpad settings that also acts on an OmniControl trackpad gesture (<see cref="TrackpadGesture.AlsoWindows"/>):
/// the words used for it on screen, what Windows does with it, where it is stored, the value that means "Nothing" (<see cref="Off"/>)
/// and the value Windows uses while the registry value is missing (<see cref="WindowsDefault"/>). <see cref="Bit"/> is the field's bit
/// in TOUCHPAD_PARAMETERS' user-settings word for <see cref="SettingRoute.TouchpadParameters"/>; <see cref="ShellId"/> is the number the
/// shell's touchpad settings service knows a <see cref="SettingRoute.Registry"/> value by (the Settings app's own table).
/// </summary>
public sealed record WindowsGestureSetting(WindowsGesture Gesture, string Name, string WindowsAction, string RegistryValue, SettingRoute Route,
    int Off, int WindowsDefault, int Bit = -1, int ShellId = -1)
{
    /// <summary>True when this raw value (null = the registry value is missing) means Windows runs its own action.</summary>
    public bool IsOn(int? value) => (value ?? WindowsDefault) != Off;
}

/// <summary>
/// The master list of Windows' touchpad settings that OmniControl's trackpad gestures meet, one entry per <see cref="WindowsGesture"/>.
/// Read by <see cref="WindowsTouchpad"/> (what Windows does now), <see cref="GestureTakeover"/> (turning one off while OmniControl uses
/// the gesture) and the real store. Values and bits were read on Windows 11 build 26300 (registry and SPI_GETTOUCHPADPARAMETERS) and
/// match the SDK's TOUCHPAD_PARAMETERS_V1 and Microsoft's touchpad tuning guide; the three-finger tap's 0 = Nothing, 1 = Open search
/// (missing = 1) come from the Settings app's choices. The shell ids (15 three-finger tap, 17 three-finger swipes, 18 four-finger swipes)
/// are the Settings app's own (<c>SettingsHandlers_nt.dll</c>, build 26300); the shell's answers for them match its cache's defaults.
/// </summary>
public static class WindowsGestureSettings
{
    public static readonly IReadOnlyList<WindowsGestureSetting> All = new WindowsGestureSetting[]
    {
        new(WindowsGesture.ThreeFingerSwipes, "three-finger swipes", "switch apps or desktops", "ThreeFingerSlideEnabled", SettingRoute.Registry, 0, 1, ShellId: 17),
        new(WindowsGesture.FourFingerSwipes, "four-finger swipes", "switch apps or desktops", "FourFingerSlideEnabled", SettingRoute.Registry, 0, 1, ShellId: 18),
        new(WindowsGesture.ThreeFingerTap, "three-finger tap", "opens search or another action", "ThreeFingerTapEnabled", SettingRoute.Registry, 0, 1, ShellId: 15),
        new(WindowsGesture.OneFingerTap, "tap to click", "clicks", "TapsEnabled", SettingRoute.TouchpadParameters, 0, 1, Bit: 2),
        new(WindowsGesture.TwoFingerTap, "two-finger tap", "right-clicks", "TwoFingerTapEnabled", SettingRoute.TouchpadParameters, 0, 1, Bit: 4),
        new(WindowsGesture.PinchToZoom, "pinch to zoom", "zooms", "ZoomEnabled", SettingRoute.TouchpadParameters, 0, 1, Bit: 8),
    };

    public static WindowsGestureSetting Of(WindowsGesture gesture)
    {
        foreach (var setting in All)
            if (setting.Gesture == gesture) return setting;
        throw new ArgumentOutOfRangeException(nameof(gesture), gesture, "No Windows setting is listed for this gesture.");
    }
}
