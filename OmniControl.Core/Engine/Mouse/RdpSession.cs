using System.Text.RegularExpressions;

namespace OmniControl.Core.Engine.Mouse;

/// Whether a Remote Desktop Connection window (mstsc) keeps shortcuts such as Alt+Tab on this PC: its "Apply Windows key combinations"
/// (keyboardhook in the .rdp file it was started with, else Documents\Default.rdp, which its connection dialog writes) is "On this computer",
/// or "Only when using the full screen" (Windows' default) while the window is not full screen.
internal static class RdpSession
{
    private const uint QueryLimited = 0x1000;
    private const int CommandLineInformation = 60; // ProcessCommandLineInformation, Windows 8.1 and later
    private const int OnThisComputer = 0, FullScreenOnly = 2;
    private const string HookSetting = "keyboardhook:i:";

    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, out int returned);

    private static readonly Regex RdpFile = new("\"([^\"]+\\.rdp)\"|(\\S+\\.rdp)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// True when the session in <paramref name="window"/> of process <paramref name="pid"/> keeps those shortcuts on this PC.
    internal static bool KeepsKeysHere(int pid, IntPtr window) => KeyboardHookOf(pid) switch
    {
        OnThisComputer => true,
        FullScreenOnly => !WindowQuery.CoversItsMonitor(window),
        _ => false,
    };

    /// The session's keyboardhook value; Windows' default when no file says otherwise or one can't be read.
    private static int KeyboardHookOf(int pid)
    {
        var file = CommandLineOf(pid) is { } line && RdpFile.Match(line) is { Success: true } m
            ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Default.rdp");
        try
        {
            if (!File.Exists(file)) return FullScreenOnly;
            var setting = File.ReadLines(file).FirstOrDefault(l => l.StartsWith(HookSetting, StringComparison.OrdinalIgnoreCase));
            return setting is not null && int.TryParse(setting.AsSpan(HookSetting.Length), out var value) ? value : FullScreenOnly;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FullScreenOnly; // locked or unreadable: Windows' default is the best guess
        }
    }

    /// The process's command line, or null when Windows does not give it.
    private static string? CommandLineOf(int pid)
    {
        var process = OpenProcess(QueryLimited, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            NtQueryInformationProcess(process, CommandLineInformation, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) return null;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (NtQueryInformationProcess(process, CommandLineInformation, buffer, needed, out _) != 0) return null;
                var text = Marshal.PtrToStructure<UnicodeString>(buffer);
                return Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(process); }
    }
}
