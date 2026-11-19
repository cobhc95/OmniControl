using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Shared.Services;

/// Tells whether a screen point is over one of OmniControl's own windows. The left and right buttons are never remapped there, so a
/// bad mapping can always be clicked away; every other button, the wheel and gestures work there as anywhere else.
public static class OwnProgram
{
    private static readonly int ProcessId = Environment.ProcessId;

    public static bool IsUnder(int x, int y) => WindowQuery.ProcessIdAt(x, y) == ProcessId;

    /// True when one of OmniControl's own windows is the active window (custom rules never run then).
    public static bool IsActive() => WindowQuery.ActiveProcessId() == ProcessId;
}
