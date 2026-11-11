using OmniDial.Core.Engine;
using OmniDial.Core.Interfaces;
using OmniDial.Core.Models;

namespace OmniDial.Core.Command;

/// <summary>
/// OmniDial command language — the single API every input funnels into.
///
///   &lt;monitor&gt; &lt;action&gt; &lt;value&gt; [&lt;monitor&gt; &lt;action&gt; &lt;value&gt; ...]
///   e.g.  PL2779Q b 36  HPA34T c -5  ALL b +2
///
/// Monitor selector: alias (e.g. PL2779Q), ALL, LCD (laptop), SYS (system volume),
/// or an index number (0, 1…). Case-insensitive. Multiple helpers may be grouped with ','.
///
/// Actions (case-insensitive):
///   b  brightness        c  contrast        s  sharpness
///   r  red gain          g  green gain      u  blue gain   (u=blue to avoid clash with b)
///   t  colour temp (K)   i  input source    v  volume      m  mute (0/1)
///   p  power (on|off)    x  factory reset   o  osd (0/1)
///   sat saturation (GPU)  bright|lcd alias for b on LCD
///
/// Value:
///   absolute number (e.g. 36), relative +n / -n, percent n% of max,
///   relative percent +n% / -n%, or named value (on/off, hdmi1/hdmi2/dp1/dp2/usbc, presets).
///
/// Examples:
///   PL2779Q b 36
///   PL2779Q b +5
///   ALL b 40
///   LCD b 70
///   SYS v 30
///   PL2779Q c - 5   (spaces around sign are tolerated)
/// </summary>
public sealed class CommandLanguage
{
    private readonly IReadOnlyList<IMonitor> _monitors;
    private readonly ILaptopBrightness _laptop;
    private readonly ISystemVolume _system;
    private readonly IGpuSaturation _gpu;

    public CommandLanguage(IReadOnlyList<IMonitor> monitors,
        ILaptopBrightness? laptop = null,
        ISystemVolume? system = null,
        IGpuSaturation? gpu = null)
    {
        _monitors = monitors;
        _laptop = laptop ?? new LaptopBrightnessController();
        _system = system ?? new SystemVolumeController();
        _gpu = gpu ?? GpuSaturationFactory.Create();
    }

    public OperationResult Execute(string command, Action<string>? log = null)
    {
        log ??= _ => { };
        if (string.IsNullOrWhiteSpace(command))
            return OperationResult.Fail("Empty command.");

        var tokens = Tokenize(command);
        if (tokens.Count == 0) return OperationResult.Fail("No tokens.");

        int i = 0;
        var overall = OperationResult.Ok();
        while (i < tokens.Count)
        {
            // monitor selector
            string monitorTok = tokens[i++];

            if (i >= tokens.Count) { overall = OperationResult.Fail($"Missing action after '{monitorTok}'."); break; }
            string actionTok = tokens[i++];

            if (i >= tokens.Count) { overall = OperationResult.Fail($"Missing value after '{monitorTok} {actionTok}'."); break; }
            string valueTok = tokens[i++];

            // tolerate "c - 5" => valueTok was "-" and next is number
            if ((valueTok == "+" || valueTok == "-") && i < tokens.Count)
            {
                valueTok = valueTok + tokens[i++];
            }

            var r = ExecuteOne(monitorTok, actionTok, valueTok);
            log($"[{monitorTok} {actionTok} {valueTok}] {(r.Success ? "OK" : "FAIL")} {r.Message}");
            if (!r.Success)
                overall = OperationResult.Fail(r.Message);
        }
        return overall;
    }

    // ------------------------------------------------------------------ internals
    private OperationResult ExecuteOne(string monitorSel, string action, string value)
    {
        string a = action.ToLowerInvariant();
        string m = monitorSel.Trim();

        // Special targets
        if (m.Equals("LCD", StringComparison.OrdinalIgnoreCase))
        {
            return a switch
            {
                "b" or "brightness" or "bright" =>
                    _laptop.SetBrightness(ParseInt(value, out int v) ? v : 50),
                _ => OperationResult.Fail($"LCD does not support action '{action}'. Use b."),
            };
        }
        if (m.Equals("SYS", StringComparison.OrdinalIgnoreCase) || m.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase))
        {
            return a switch
            {
                "v" or "volume" =>
                    _system.SetVolume(ParseInt(value, out int v) ? v : 50),
                _ => OperationResult.Fail($"SYS does not support action '{action}'. Use v."),
            };
        }
        if (m.Equals("GPU", StringComparison.OrdinalIgnoreCase))
        {
            return a switch
            {
                "sat" or "saturation" or "s" =>
                    _gpu.SetSaturation(ParseInt(value, out int v) ? v : 50),
                _ => OperationResult.Fail($"GPU does not support action '{action}'. Use sat."),
            };
        }

        var targets = ResolveMonitors(m);
        if (targets.Count == 0)
            return OperationResult.Fail($"Unknown monitor '{m}'. Known: ALL, LCD, SYS, GPU, " +
                string.Join(", ", _monitors.Select(x => x.Info.Alias)));

        OperationResult last = OperationResult.Ok();
        foreach (var mon in targets)
        {
            last = ApplyToMonitor(mon, a, value);
            if (!last.Success) break;
        }
        return last;
    }

    private List<IMonitor> ResolveMonitors(string selector)
    {
        if (selector.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            return _monitors.ToList();

        // comma-separated group
        if (selector.Contains(','))
            return selector.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(ResolveMonitors).Distinct().ToList();

        // numeric index
        if (int.TryParse(selector, out int idx) && idx >= 0 && idx < _monitors.Count)
            return new List<IMonitor> { _monitors[idx] };

        // alias / friendly substring (case-insensitive)
        var hit = _monitors.FirstOrDefault(x =>
            x.Info.Alias.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
            x.Info.FriendlyName.Contains(selector, StringComparison.OrdinalIgnoreCase));
        return hit != null ? new List<IMonitor> { hit } : new List<IMonitor>();
    }

    private OperationResult ApplyToMonitor(IMonitor mon, string action, string rawValue)
    {
        bool isRelative = rawValue.StartsWith("+") || rawValue.StartsWith("-");
        bool isPercent = rawValue.EndsWith("%");
        string core = rawValue.TrimEnd('%');

        int Resolve(byte vcp, int fallback)
        {
            if (!isRelative && !isPercent && int.TryParse(core, out int abs)) return abs;
            var cur = mon.GetVcp(vcp);
            int max = cur.HasValue ? (int)cur.Value.Maximum : 100;
            int curVal = cur.HasValue ? (int)cur.Value.Current : fallback;
            if (isPercent)
            {
                return (int)Math.Round(max * Parse(core) / 100.0);
            }
            if (isRelative)
                return curVal + (int)Parse(core);
            return fallback;
        }

        return action switch
        {
            "b" or "brightness" or "bright" => mon.SetBrightness(Resolve(VcpCode.Brightness, 50)),
            "c" or "contrast"                => mon.SetContrast(Resolve(VcpCode.Contrast, 50)),
            "s" or "sharpness"               => mon.SetSharpness(Resolve(VcpCode.Sharpness, 50)),
            "r" or "red"                     => mon.SetRedGain(Resolve(VcpCode.RedGain, 100)),
            "g" or "green"                   => mon.SetGreenGain(Resolve(VcpCode.GreenGain, 100)),
            "u" or "blue"                    => mon.SetBlueGain(Resolve(VcpCode.BlueGain, 100)),
            "t" or "temp" or "colortemp"     => mon.SetColorTemperature(Resolve(VcpCode.ColorTemperature, 6500)),
            "v" or "volume"                  => mon.SetVolume(Resolve(VcpCode.Volume, 50)),
            "m" or "mute"                    => mon.SetMute(rawValue.Equals("1") || rawValue.Equals("on", StringComparison.OrdinalIgnoreCase)),
            "x" or "reset" or "factory"      => mon.FactoryReset(),
            "p" or "power"                   => rawValue.Equals("on", StringComparison.OrdinalIgnoreCase) || rawValue == "1"
                ? mon.SetPower(true) : mon.SetPower(false),
            "i" or "input" =>
                TryParseInput(rawValue, out uint src)
                    ? mon.SetInputSource(src)
                    : OperationResult.Fail($"Bad input '{rawValue}'. Use hdmi1/hdmi2/dp1/dp2/usbc/vga/dvi or 0x11."),
            "o" or "osd"                     => mon.TrySetVcp(VcpCode.OSD, rawValue == "1" ? 1u : 0u),
            _ => OperationResult.Fail($"Unknown action '{action}'. Actions: b c s r g u t v m x p i o"),
        };
    }

    private static bool TryParseInput(string s, out uint src)
    {
        src = 0;
        string norm = s.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
        switch (norm)
        {
            case "vga": case "0x01": src = VcpCode.Input.Vga; return true;
            case "dvi1": case "dvi": src = VcpCode.Input.Dvi1; return true;
            case "dvi2": src = VcpCode.Input.Dvi2; return true;
            case "hdmi1": case "hdmi": case "0x11": src = VcpCode.Input.Hdmi1; return true;
            case "hdmi2": case "0x12": src = VcpCode.Input.Hdmi2; return true;
            case "hdmi3": case "0x13": src = VcpCode.Input.Hdmi3; return true;
            case "dp1": case "dp": case "0x1a": src = VcpCode.Input.Dp1; return true;
            case "dp2": case "0x1b": src = VcpCode.Input.Dp2; return true;
            case "usbc": case "usb": case "0x1e": src = VcpCode.Input.UsbC; return true;
            default:
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    uint.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out uint hex))
                { src = hex; return true; }
                return false;
        }
    }

    private static bool ParseInt(string s, out int v) =>
        int.TryParse(s.TrimEnd('%'), System.Globalization.NumberStyles.Integer, null, out v);

    private static double Parse(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Any, null, out double d) ? d : 0;

    private static List<string> Tokenize(string command)
    {
        // split on whitespace, keep signs attached where possible; "PL2779Q b 36" -> 3 tokens
        return command.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
