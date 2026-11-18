using System.Runtime.InteropServices;

namespace OmniControl.Core.Native;

/// <summary>powrprof's power capabilities: whether this PC sleeps in Modern Standby, read for the wake facts.</summary>
internal static class WakeNative
{
    private const int SystemPowerCapabilities = 4, CapabilitiesSize = 76, AoAcOffset = 20;

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength, byte[] output, uint outputLength);

    /// <summary>True when Windows says the PC uses Modern Standby (S0 low power idle); false when it does not or the question failed.</summary>
    public static bool IsModernStandby()
    {
        var buffer = new byte[CapabilitiesSize];
        return CallNtPowerInformation(SystemPowerCapabilities, IntPtr.Zero, 0, buffer, (uint)buffer.Length) == 0 && buffer[AoAcOffset] != 0;
    }
}
