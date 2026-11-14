using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using OmniControl.Core.Engine.Pointer;

namespace OmniControl.Core.Engine.Trackpad;

/// <summary>
/// Windows' own touchpad settings, read and written the way the Settings app keeps them (findings in HANDOVER.md, "Trackpad gesture
/// takeover" and "Trackpad takeover: the shell's own copy"). Tap to click, two-finger tap and pinch to zoom are fields of
/// TOUCHPAD_PARAMETERS: SPI_SETTOUCHPADPARAMETERS saves them and Windows' input applies them at once. Three/four-finger swipes and the
/// three-finger tap are registry values under <c>HKCU\...\PrecisionTouchPad</c> that the shell acts on from its own copy: after writing
/// one, the shell is told to refresh it (<see cref="ShellTouchpadSettings"/>), as the Settings app does.
/// </summary>
public sealed class TouchpadSettingsStore : ITouchpadSettingsStore
{
    // TOUCHPAD_PARAMETERS_V1 (WinUser.h, SDK 10.0.26100): versionNumber, maxSupportedContacts, legacyTouchpadFeatures, the status
    // bit word, the user-settings bit word at byte 16, then sensitivityLevel and five UINTs: 44 bytes.
    private const int ParametersSize = 44, ParametersVersion = 1, UserBitsOffset = 16;

    public string? Read(WindowsGestureSetting setting, out int? value)
    {
        value = null;
        if (setting.Route == SettingRoute.Registry)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(WindowsTouchpad.Key);
                value = key?.GetValue(setting.RegistryValue) as int?;
                return null;
            }
            catch (Exception e) when (e is SecurityException or IOException or UnauthorizedAccessException)
            {
                return $"Windows didn't let OmniControl read {setting.Name} ({e.Message}).";
            }
        }
        if (ReadParameters() is not { } parameters) return $"Windows didn't say whether {setting.Name} is on ({LastError()}).";
        value = (UserBits(parameters) & (1 << setting.Bit)) != 0 ? 1 : 0;
        return null;
    }

    public string? Write(WindowsGestureSetting setting, int? value) =>
        setting.Route == SettingRoute.Registry ? WriteRegistry(setting, value) : WriteParameter(setting, (value ?? setting.WindowsDefault) != 0);

    public string? Announce(WindowsGestureSetting setting) =>
        setting.ShellId < 0 ? null : ShellTouchpadSettings.Refresh(setting.ShellId);

    public string? ReadInEffect(WindowsGestureSetting setting, out int? value)
    {
        if (setting.ShellId < 0) return Read(setting, out value);
        var problem = ShellTouchpadSettings.Read(setting.ShellId, out var used);
        value = problem is null ? used : null;
        return problem;
    }

    private static string? WriteRegistry(WindowsGestureSetting setting, int? value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(WindowsTouchpad.Key);
            if (value is { } v) key.SetValue(setting.RegistryValue, v, RegistryValueKind.DWord);
            else key.DeleteValue(setting.RegistryValue, throwOnMissingValue: false);
            return null;
        }
        catch (Exception e) when (e is SecurityException or IOException or UnauthorizedAccessException)
        {
            return $"Windows didn't let OmniControl change {setting.Name} ({e.Message}).";
        }
    }

    private static string? WriteParameter(WindowsGestureSetting setting, bool on)
    {
        if (ReadParameters() is not { } parameters) return $"Windows didn't say whether {setting.Name} is on ({LastError()}).";
        var bits = UserBits(parameters);
        bits = on ? bits | (1 << setting.Bit) : bits & ~(1 << setting.Bit);
        BitConverter.TryWriteBytes(parameters.AsSpan(UserBitsOffset, 4), bits);
        return SpiNative.Bytes(SpiNative.SetTouchpadParameters, ParametersSize, parameters, SpiNative.SaveAndBroadcast)
            ? null
            : $"Windows didn't let OmniControl change {setting.Name} ({LastError()}).";
    }

    private static byte[]? ReadParameters()
    {
        var parameters = new byte[ParametersSize];
        BitConverter.TryWriteBytes(parameters.AsSpan(0, 4), ParametersVersion);
        return SpiNative.Bytes(SpiNative.GetTouchpadParameters, ParametersSize, parameters, 0) ? parameters : null;
    }

    private static int UserBits(byte[] parameters) => BitConverter.ToInt32(parameters, UserBitsOffset);

    private static string LastError() => new Win32Exception(Marshal.GetLastWin32Error()).Message;
}
