using Avalonia.Input;

namespace OmniControl.App.Services;

/// <summary>Wrap-around stepping through a list (AGENTS.md rule 5: Up/Down wraps at both ends): past the last item is the first, before the first is the last.</summary>
public static class Wrap
{
    /// <param name="index">The wanted index, which may be before the start or past the end by any amount.</param>
    /// <param name="count">How many items there are (more than 0).</param>
    public static int Index(int index, int count) => (index % count + count) % count;

    /// <summary>The step an arrow key stands for: Down is +1, Up is -1.</summary>
    public static int Direction(Key key) => key == Key.Down ? 1 : -1;
}
