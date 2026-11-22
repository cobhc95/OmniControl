using System.Management;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace OmniControl.Core.Engine.Resources;

/// Installs Windows Packet Filter's driver when a download limit first needs it or the person retries (Setup's optional task does the same):
/// the official, unmodified Windows 10+ x64 driver files in the app folder are checked against their pinned SHA-256 while held open, Windows'
/// own netcfg adds the filter (as the driver's own MSI does; Windows asks only when OmniControl is not already administrator), and the
/// same elevated step records that it did under HKLM (whose access list lets only administrators and SYSTEM change it), so OmniControl's
/// uninstall removes only its own copy. <see cref="Attach"/> takes it out of (or puts it back into) every adapter's traffic path for the
/// "Packet filter" switch. Call off the UI thread.
public static class PacketFilterSetup
{
    // Retyped in OmniControl.App.csproj (the copy into the app folder) and tools/setup.iss (install, uninstall and the record).
    internal const string Folder = "windows-packet-filter", Inf = "ndisrd_lwf.inf";
    private const string ComponentId = "nt_ndisrd";
    private const string RecordKey = @"SOFTWARE\OmniControl", RecordValue = "PacketFilterInstalled";

    private static readonly (string File, string Sha256)[] Files =
    {
        (Inf, "71DBE4B33F3654F3E37D27F507D1CD6ADE70FB9C1A327435155C6C71DD137654"),
        ("ndisrd.sys", "8623A9C34531CF73DE4286AD518B84AF03A331C78959769A1CA38723E3258984"),
        ("ndisrd.cat", "43FE8866894919CB8B7B4729CE8910D7D4C416260465EDDA6FCE68396DE83629"),
    };

    /// Installs the driver and waits; null when it is there and recorded afterwards, else one plain sentence saying what went wrong.
    public static string? Install()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, Folder);
        var held = new List<FileStream>();
        try
        {
            // Held open for reading only while netcfg runs, so nothing can swap a checked file before Windows reads it.
            foreach (var (name, sha256) in Files)
            {
                var file = new FileStream(Path.Combine(folder, name), FileMode.Open, FileAccess.Read, FileShare.Read);
                held.Add(file);
                if (Convert.ToHexString(SHA256.HashData(file)) != sha256) return NotInstalled("its driver files are not the expected ones");
            }
            // netcfg's exit code is not only 0 on success (a restart it asks for), so the driver's service key decides the receipt.
            var result = Elevated($"& {Quoted(SystemTools.NetCfg)} -v -l {Quoted(Path.Combine(folder, Inf))} -c s -i {ComponentId}; $code = $LASTEXITCODE; "
                + $"if (Test-Path -LiteralPath {Quoted(DriverPath)}) {{ New-Item -Path {Quoted(RecordPath)} -ErrorAction SilentlyContinue | Out-Null; "
                + $"Set-ItemProperty -Path {Quoted(RecordPath)} -Name {RecordValue} -Value 1 -Type DWord }}; exit $code");
            if (result.Failure != HelperFailure.None || !PacketFilter.Installed) return NotInstalled(Says(result));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return NotInstalled("its driver files could not be read");
        }
        finally
        {
            foreach (var file in held) file.Dispose();
        }
        return Recorded();
    }

    private static string NotInstalled(string why) => $"Windows Packet Filter was not installed: {why}.";

    /// Attaches Windows Packet Filter's filter to every network adapter or detaches it from all (Windows' own binding switch, as
    /// Enable/Disable-NetAdapterBinding; a detached filter is out of the adapter's traffic path), then reads Windows back. Windows asks only
    /// when OmniControl is not administrator; nothing runs when Windows already has it so. Null when done, else one plain sentence. Call off the UI thread.
    public static string? Attach(bool on)
    {
        string? why;
        try
        {
            if (!PacketFilter.Installed || IsAttached(on)) return null;
            var result = Elevated($"{(on ? "Enable" : "Disable")}-NetAdapterBinding -Name '*' -ComponentID {ComponentId} -IncludeHidden -AllBindings -ErrorAction SilentlyContinue");
            why = result.Failure != HelperFailure.None ? Says(result) : IsAttached(on) ? null : "some network adapters still have it the other way";
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            why = "Windows' list of network adapter bindings could not be read";
        }
        return why is null ? null : $"Windows Packet Filter was not switched {(on ? "on" : "off")}: {why}.";
    }

    /// True when every network adapter Windows lists has the filter attached (<paramref name="on"/>) or detached; a read, no rights needed.
    /// Throws a <see cref="ManagementException"/> or <see cref="COMException"/> when Windows' list cannot be read.
    private static bool IsAttached(bool on)
    {
        using var searcher = new ManagementObjectSearcher(@"\\.\root\StandardCimv2", $"SELECT Enabled FROM MSFT_NetAdapterBindingSettingData WHERE ComponentID = '{ComponentId}'");
        using var found = searcher.Get();
        var all = true;
        foreach (var binding in found.OfType<ManagementObject>())
            using (binding)
                all &= binding["Enabled"] is bool enabled && enabled == on;
        return all;
    }

    private static string Says(HelperResult result) => result.Failure switch
    {
        HelperFailure.Cancelled => "the administrator prompt was refused",
        HelperFailure.Blocked => "Windows blocked its set-up",
        HelperFailure.StillRunning => "Windows' network set-up is still running",
        HelperFailure.NotStarted => "Windows' network set-up did not start",
        _ when result.ExitCode == 0 => "Windows' network set-up ended without adding it",
        _ => $"Windows' network set-up ended with code {result.ExitCode}",
    };

    private const string RecordPath = $@"HKLM:\{RecordKey}", DriverPath = $@"HKLM:\{PacketFilter.ServiceKey}";

    /// Runs a PowerShell script as administrator (Windows asks only when OmniControl is not already) and waits.
    private static HelperResult Elevated(string script) => HelperProcess.RunElevated(SystemTools.PowerShell,
        $"-NoProfile -NonInteractive -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}", hidden: true);

    private static string Quoted(string text) => $"'{text.Replace("'", "''")}'";

    private static string? Recorded()
    {
        const string Unrecorded = "Windows Packet Filter was installed, but uninstalling OmniControl will leave it.";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RecordKey);
            return key?.GetValue(RecordValue) is 1 ? null : Unrecorded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Unrecorded;
        }
    }
}
