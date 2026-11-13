namespace OmniControl.Core.Models;

/// <summary>Structured result of a VCP read.</summary>
public readonly record struct VcpValue(uint Current, uint Maximum, uint Type /* 0=NC,1=RO,2=RW */, bool IsMomentary)
{
    public static VcpValue Unsupported => new(0, 0, 0, false);

    /// <summary>The current value as a 0-100 share of the maximum (0 when the maximum is unknown).</summary>
    public int Percent => Models.Percent.Of(Current, Maximum);
}
