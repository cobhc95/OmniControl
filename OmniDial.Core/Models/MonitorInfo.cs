namespace OmniDial.Core.Models;

/// <summary>Descriptor for a physical DDC/CI monitor, as enumerated via dxva2.</summary>
public sealed class MonitorInfo
{
    public required string Description { get; init; }   // from PHYSICAL_MONITOR e.g. "\??\DISPLAY#..."
    public required string DeviceName  { get; init; }   // e.g. \\.\DISPLAY1
    public required string FriendlyName{ get; init; }   // best-effort model string
    public required string InstanceId  { get; init; }   // PNP device instance (for stable identity)
    public required int    Index       { get; init; }
    public string? Capabilities { get; set; }           // raw MCCS capabilities string (lazy)

    /// <summary>Alias used by the command language, e.g. "PL2779Q". Defaults to friendly name without spaces.</summary>
    public string Alias { get; set; } = string.Empty;

    public bool IsLaptopLcd => InstanceId.StartsWith("DISPLAY\\", StringComparison.OrdinalIgnoreCase)
                               || DeviceName.Contains("Laptop", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Alias)
            ? $"{Index}: {FriendlyName} [{DeviceName}]"
            : $"{Alias} ({FriendlyName}) [{DeviceName}]";
}

/// <summary>Structured result of a VCP read.</summary>
public readonly record struct VcpValue(uint Current, uint Maximum, uint Type /* 0=NC,1=RO,2=RW */, bool IsMomentary)
{
    public static VcpValue Unsupported => new(0, 0, 0, false);
}

/// <summary>Common throttle / result reporting.</summary>
public sealed class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public static OperationResult Ok(string msg = "") => new() { Success = true, Message = msg };
    public static OperationResult Fail(string msg) => new() { Success = false, Message = msg };
}
