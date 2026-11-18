using System.Text;

namespace OmniControl.Core.Engine.Wake;

/// <summary>
/// Turns on the settings a wake needs, on this PC, behind Windows' own administrator prompt (the user can refuse it). The script is built from
/// fixed text and the card's own address, never from text another PC sent. Each fix tries its sources in order inside the one elevated step
/// (Windows' own API, then powercfg with the exact device name, then the driver's registry keyword) and checks after each that it took.
/// Call off the UI thread; it waits for the prompt and the script.
/// </summary>
public static class WakeFixer
{
    public static bool TryParse(string? text, out WakeFix fix) => Enum.TryParse(text, out fix) && Enum.IsDefined(fix);

    /// <summary>True when the setting is off now, so turning it on changes something (and is worth recording to put back).</summary>
    public static bool Changes(WakeFix fix, WakeFacts facts) => fix switch
    {
        WakeFix.MagicPacket => !facts.MagicPacketOn,
        WakeFix.Arm => !facts.Armed,
        WakeFix.MagicOnly => !facts.MagicOnly,
        _ => facts.FastStartupOn,
    };

    public static HelperResult Apply(IReadOnlyCollection<WakeFix> fixes, string mac) => Run(fixes, mac, Command);

    /// <summary>Puts the given settings back to what they were before OmniControl turned them on (one elevated step).</summary>
    public static HelperResult Revert(IReadOnlyCollection<WakeFix> fixes, string mac) => Run(fixes, mac, Undo);

    private static HelperResult Run(IReadOnlyCollection<WakeFix> fixes, string mac, Func<WakeFix, string> command)
    {
        if (MagicPacket.ParseMac(mac) is null || fixes.Count == 0) return new HelperResult(-1, HelperFailure.NotStarted, "No card to change.");
        var script = new StringBuilder();
        script.Append("$failed = $false; $card = Get-NetAdapter -Physical | Where-Object { ($_.MacAddress -replace '-', ':') -eq '").Append(mac).Append("' } | Select-Object -First 1; ");
        script.Append("if (-not $card) { exit 2 }; ");
        script.Append(Helpers());
        foreach (var fix in fixes.Distinct()) script.Append("try { ").Append(command(fix)).Append(" } catch { $failed = $true }; ");
        script.Append("if ($failed) { exit 1 }");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
        return HelperProcess.RunElevated("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}");
    }

    // The checks and the registry fallback every fix shares. The keyword names and descriptions come from WakeKeywords, as the reader's do.
    private static string Helpers() =>
        "Add-Type -Namespace Wake -Name Power -MemberDefinition '[System.Runtime.InteropServices.DllImport(\"powrprof.dll\", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool DevicePowerSetDeviceState(string name, uint flags, System.IntPtr data);'; "
        + "$name = $card.InterfaceDescription; "
        + "function IsArmed { [bool]((powercfg /devicequery wake_armed) -contains $name) }; "
        + "function DriverKey { Get-ChildItem 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Class\\" + WakeKeywords.AdapterClass + "' -ErrorAction SilentlyContinue | Where-Object { (Get-ItemProperty $_.PSPath -Name NetCfgInstanceId -ErrorAction SilentlyContinue).NetCfgInstanceId -eq $card.InterfaceGuid } | Select-Object -First 1 }; "
        + "function SetKeyword($names, $description, $value) { $key = DriverKey; if (-not $key) { throw 'no driver key' }; $done = $false; $all = @($names); "
        + "$params = Join-Path $key.PSPath 'Ndi\\Params'; if (Test-Path $params) { foreach ($sub in Get-ChildItem $params) { $text = (Get-ItemProperty $sub.PSPath -Name ParamDesc -ErrorAction SilentlyContinue).ParamDesc; if ($text -match $description) { $all += $sub.PSChildName } } }; "
        + "foreach ($n in ($all | Select-Object -Unique)) { if ($null -ne (Get-ItemProperty $key.PSPath -Name $n -ErrorAction SilentlyContinue).$n) { Set-ItemProperty $key.PSPath -Name $n -Value $value -Type String; $done = $true } }; "
        + "if (-not $done) { throw 'no keyword' } }; "
        + $"$magicNames = @({Quoted(WakeKeywords.MagicNames)}); $magicDescription = '{WakeKeywords.MagicDescription}'; "
        + $"$patternNames = @({Quoted(WakeKeywords.PatternNames)}); $patternDescription = '{WakeKeywords.PatternDescription}'; ";

    private static string Quoted(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"'{n}'"));

    private static string Command(WakeFix fix) => fix switch
    {
        WakeFix.MagicPacket =>
            "try { Set-NetAdapterPowerManagement -Name $card.Name -WakeOnMagicPacket Enabled -ErrorAction Stop } catch { }; "
            + "if ((Get-NetAdapterPowerManagement -Name $card.Name -ErrorAction SilentlyContinue).WakeOnMagicPacket -ne 'Enabled') { SetKeyword $magicNames $magicDescription '1' };",
        WakeFix.MagicOnly =>
            "try { Set-NetAdapterPowerManagement -Name $card.Name -WakeOnPattern Disabled -ErrorAction Stop } catch { }; "
            + "if ((Get-NetAdapterPowerManagement -Name $card.Name -ErrorAction SilentlyContinue).WakeOnPattern -eq 'Enabled') { SetKeyword $patternNames $patternDescription '0' };",
        WakeFix.Arm =>
            "try { [void][Wake.Power]::DevicePowerSetDeviceState($name, 1, [System.IntPtr]::Zero) } catch { }; "
            + "if (-not (IsArmed)) { powercfg /deviceenablewake \"$name\" | Out-Null }; "
            + "if (-not (IsArmed)) { $key = DriverKey; $caps = (Get-ItemProperty $key.PSPath -Name PnPCapabilities -ErrorAction SilentlyContinue).PnPCapabilities; if ($null -ne $caps) { Set-ItemProperty $key.PSPath -Name PnPCapabilities -Value ($caps -band (-bnot 0x20)) -Type DWord } }; "
            + "if (-not (IsArmed)) { throw 'not armed' };",
        _ => "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power' -Name HiberbootEnabled -Value 0 -Type DWord -ErrorAction Stop;",
    };

    // The opposite of each fix: the old value was "off" (that is the only case that is recorded).
    private static string Undo(WakeFix fix) => fix switch
    {
        WakeFix.MagicPacket =>
            "try { Set-NetAdapterPowerManagement -Name $card.Name -WakeOnMagicPacket Disabled -ErrorAction Stop } catch { }; "
            + "if ((Get-NetAdapterPowerManagement -Name $card.Name -ErrorAction SilentlyContinue).WakeOnMagicPacket -eq 'Enabled') { SetKeyword $magicNames $magicDescription '0' };",
        WakeFix.MagicOnly =>
            "try { Set-NetAdapterPowerManagement -Name $card.Name -WakeOnPattern Enabled -ErrorAction Stop } catch { }; "
            + "if ((Get-NetAdapterPowerManagement -Name $card.Name -ErrorAction SilentlyContinue).WakeOnPattern -ne 'Enabled') { SetKeyword $patternNames $patternDescription '1' };",
        WakeFix.Arm =>
            "try { [void][Wake.Power]::DevicePowerSetDeviceState($name, 2, [System.IntPtr]::Zero) } catch { }; "
            + "if (IsArmed) { powercfg /devicedisablewake \"$name\" | Out-Null }; "
            + "if (IsArmed) { throw 'still armed' };",
        _ => "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power' -Name HiberbootEnabled -Value 1 -Type DWord -ErrorAction Stop;",
    };
}
