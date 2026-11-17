using System.ComponentModel;
using System.Diagnostics;

namespace OmniControl.Core.Engine.Tailscale;

/// <summary>The one Windows Firewall rule that lets the other PCs on the tailnet reach OmniControl: reading it is free, adding it is Windows' own administrator prompt.</summary>
public static class TailscaleFirewall
{
    private const string RuleName = "OmniControl on Tailscale";

    /// <summary>Tailscale's own address ranges (IPv4 and IPv6): the rule lets in nothing else.</summary>
    private const string TailnetRanges = "100.64.0.0/10,fd7a:115c:a1e0::/48";

    private static string Netsh => Path.Combine(Environment.SystemDirectory, "netsh.exe");

    /// <summary>True when the rule exists (netsh's exit code says so in every language). Call off the UI thread.</summary>
    public static bool IsAllowed()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Netsh, $"advfirewall firewall show rule name=\"{RuleName}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            if (process is null) return false;
            _ = process.StandardOutput.ReadToEndAsync();
            return process.WaitForExit(CoreTiming.TailscaleCliWaitMs) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Adds the rule for <paramref name="port"/> (the person answers Windows' administrator prompt). Call off the UI thread.</summary>
    public static HelperResult Allow(int port) =>
        HelperProcess.RunElevated(Netsh, $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port} remoteip={TailnetRanges} profile=any");
}
