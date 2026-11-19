namespace OmniControl.Core.Native;

/// Which devices may wake the PC, from Windows' own lists: powrprof's device power list and, because that list leaves out some cards (a Realtek
/// Wi-Fi card that powercfg shows as armed is missing from it), powercfg /devicequery itself, which only reads.
internal static class DevicePowerNative
{
    private const uint FilterWakeEnabled = 0x00000002, FilterWakeProgrammable = 0x00000004, MaxNames = 512;
    private const int ErrorNoMoreItems = 259;

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool DevicePowerEnumDevices(uint queryIndex, uint interpretationFlags, uint queryFlags, byte[]? buffer, ref uint bufferSize);

    /// The friendly names of the devices Windows lets wake the PC (device power list), or null when the question failed.
    public static HashSet<string>? WakeEnabledNames() => Names(FilterWakeEnabled);

    /// The friendly names of the devices that can be allowed to wake the PC (device power list), or null when the question failed.
    public static HashSet<string>? WakeProgrammableNames() => Names(FilterWakeProgrammable);

    /// The same as <see cref="WakeEnabledNames"/> from <c>powercfg /devicequery wake_armed</c>, or null when it failed or took longer than <paramref name="waitMs"/>.
    public static HashSet<string>? PowercfgWakeArmed(int waitMs) => Powercfg("wake_armed", waitMs);

    /// The same as <see cref="WakeProgrammableNames"/> from <c>powercfg /devicequery wake_programmable</c>.
    public static HashSet<string>? PowercfgWakeProgrammable(int waitMs) => Powercfg("wake_programmable", waitMs);

    private static HashSet<string>? Names(uint filter)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (uint index = 0; index < MaxNames; index++)
        {
            uint size = 0;
            DevicePowerEnumDevices(index, 0, filter, null, ref size); // asks for the size of this name
            var error = Marshal.GetLastWin32Error();
            if (size == 0) return error == ErrorNoMoreItems || error == 0 ? names : null;
            var buffer = new byte[size];
            if (!DevicePowerEnumDevices(index, 0, filter, buffer, ref size)) return null;
            names.Add(System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0'));
        }
        return names;
    }

    private static HashSet<string>? Powercfg(string query, int waitMs)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powercfg.exe", $"/devicequery {query}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(waitMs))
            {
                process.Kill(true); // a hung powercfg must not hold the read
                return null;
            }
            if (process.ExitCode != 0 || !output.Wait(waitMs)) return null;
            return new HashSet<string>(output.Result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l != "NONE"), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or AggregateException)
        {
            return null; // not available here: the caller tries the next source
        }
    }
}
