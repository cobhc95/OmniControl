using System.Text;

namespace OmniControl.Core.Engine.Wake;

/// <summary>
/// Turns on the settings a wake needs, on this PC, behind Windows' own administrator prompt (the user can refuse it). The script is built from
/// fixed text and the card's own address, never from text another PC sent. Call off the UI thread; it waits for the prompt and the script.
/// </summary>
public static class WakeFixer
{
    public static bool TryParse(string? text, out WakeFix fix) => Enum.TryParse(text, out fix) && Enum.IsDefined(fix);

    public static HelperResult Apply(IReadOnlyCollection<WakeFix> fixes, string mac)
    {
        if (MagicPacket.ParseMac(mac) is null || fixes.Count == 0) return new HelperResult(-1, HelperFailure.NotStarted, "No card to change.");
        var script = new StringBuilder();
        script.Append("$failed = $false; $card = Get-NetAdapter -Physical | Where-Object { ($_.MacAddress -replace '-', ':') -eq '").Append(mac).Append("' } | Select-Object -First 1; ");
        script.Append("if (-not $card) { exit 2 }; ");
        foreach (var fix in fixes.Distinct()) script.Append("try { ").Append(Command(fix)).Append(" } catch { $failed = $true }; ");
        script.Append("if ($failed) { exit 1 }");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
        return HelperProcess.RunElevated("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}");
    }

    private static string Command(WakeFix fix) => fix switch
    {
        WakeFix.MagicPacket => "Set-NetAdapterPowerManagement -Name $card.Name -WakeOnMagicPacket Enabled -ErrorAction Stop;",
        WakeFix.MagicOnly => "Set-NetAdapterPowerManagement -Name $card.Name -WakeOnPattern Disabled -ErrorAction Stop;",
        WakeFix.Arm => "powercfg /deviceenablewake $card.InterfaceDescription; if ($LASTEXITCODE -ne 0) { throw 'powercfg' };",
        _ => "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power' -Name HiberbootEnabled -Value 0 -Type DWord -ErrorAction Stop;",
    };
}
