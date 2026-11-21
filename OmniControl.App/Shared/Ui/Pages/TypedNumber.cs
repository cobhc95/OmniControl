using System.Globalization;

namespace OmniControl.App.Shared.Ui.Pages;

/// A value whose typed form has words of its own (a unit, empty = off): how it is offered for typing and read back. A
/// <see cref="NumberText"/> with one uses it instead of <see cref="TypedNumber"/>.
public interface ITypedValue
{
    string Show(double value);

    /// False: refused, the value stays as it was.
    bool TryRead(string? typed, out double value);
}

/// The one rule for a number typed into a box (a slider's value, a number setting of an action): a whole number, or a number with
/// up to the allowed decimals, held within the limits; anything else is refused and the value stays as it was.
public static class TypedNumber
{
    public static bool TryRead(string? typed, int min, int max, out int value)
    {
        value = 0;
        if (!int.TryParse(typed?.Trim(), out var number)) return false;
        value = Math.Clamp(number, min, max);
        return true;
    }

    /// A number with up to <paramref name="decimals"/> decimals (more are rounded off); with none, only a whole number is taken. The
    /// user's own decimal mark is read first, then a point.
    public static bool TryRead(string? typed, double min, double max, int decimals, out double value)
    {
        value = 0;
        var text = typed?.Trim();
        if (decimals == 0)
        {
            if (!TryRead(text, (int)Math.Ceiling(min), (int)Math.Floor(max), out var whole)) return false;
            value = whole;
            return true;
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return false;
        if (!double.IsFinite(number)) return false;
        value = Math.Clamp(Math.Round(number, decimals), min, max);
        return true;
    }

    /// A value as it is offered for typing: up to <paramref name="decimals"/> decimals, without trailing zeros.
    public static string Show(double value, int decimals) =>
        value.ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), CultureInfo.CurrentCulture);
}
