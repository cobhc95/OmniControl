using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// Stepping the pointer speed through a list the user chose (Windows' 1-20 scale).
public static class CursorSpeed
{
    /// Moves to the next speed above the current one in <paramref name="speedsText"/> ("4,8,12"), wrapping to the lowest.
    public static void Cycle(string speedsText)
    {
        var speeds = speedsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n >= SystemCommands.MinSpeed && n <= SystemCommands.MaxSpeed)
            .Distinct().Order().ToList();
        if (speeds.Count == 0 || SystemCommands.GetCursorSpeed() is not { } now) return;
        SystemCommands.SetCursorSpeed(speeds.FirstOrDefault(s => s > now, speeds[0]));
    }
}
