namespace OmniControl.Core.Models;

/// One display attached to the desktop, found without talking to the monitor itself (so it is cheap to list).
/// <param name="DeviceName">Windows' name for the display ("\.\DISPLAY1"): what a gamma ramp is set on.</param>
/// <param name="Name">The monitor's model name (or "Built-in display" for a laptop panel), made unique among the displays.</param>
/// <param name="DevicePath">Windows' path of the monitor device; tells apart two monitors that share a name.</param>
public sealed record DisplayInfo(string DeviceName, string Name, string DevicePath, bool IsInternal, bool IsPrimary);
