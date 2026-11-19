using Avalonia.Input;

namespace OmniControl.App.Shared.Services;

/// Wrap-around stepping through a list: past the last item is the first, before the first is the last.
public static class Wrap
{
    /// <param name="index">The wanted index, which may be before the start or past the end by any amount.</param>
    /// <param name="count">How many items there are (more than 0).</param>
    public static int Index(int index, int count) => (index % count + count) % count;

    /// The step an arrow key stands for: Down is +1, Up is -1.
    public static int Direction(Key key) => key == Key.Down ? 1 : -1;
}
