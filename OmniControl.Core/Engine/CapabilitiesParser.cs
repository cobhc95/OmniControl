using System.Globalization;

namespace OmniControl.Core.Engine;

/// <summary>Reads the vcp(...) part of a monitor's MCCS capabilities string: "vcp(10 12 14(01 05 0B) 60(0F 11 12) D6(01 04))".</summary>
internal static class CapabilitiesParser
{
    private const string Section = "vcp(";

    /// <summary>The codes the string lists, each with the values it allows (empty for a continuous control).</summary>
    public static Dictionary<byte, List<uint>> Vcp(string capabilities)
    {
        var codes = new Dictionary<byte, List<uint>>();
        var at = capabilities.IndexOf(Section, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return codes;

        var i = at + Section.Length;
        while (i < capabilities.Length && capabilities[i] != ')')
        {
            if (!Uri.IsHexDigit(capabilities[i])) { i++; continue; }

            var code = ReadHex(capabilities, ref i);
            var values = new List<uint>();
            if (i < capabilities.Length && capabilities[i] == '(')
            {
                i++;
                var depth = 1;
                while (i < capabilities.Length && depth > 0)
                {
                    var c = capabilities[i];
                    if (c == '(') { depth++; i++; }
                    else if (c == ')') { depth--; i++; }
                    else if (depth == 1 && Uri.IsHexDigit(c)) values.Add(ReadHex(capabilities, ref i));
                    else i++;
                }
            }
            if (code <= byte.MaxValue) codes[(byte)code] = values;
        }
        return codes;
    }

    private static uint ReadHex(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && Uri.IsHexDigit(text[i])) i++;
        return uint.TryParse(text.AsSpan(start, i - start), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : uint.MaxValue;
    }
}
