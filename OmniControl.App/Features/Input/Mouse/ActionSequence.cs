namespace OmniControl.App.Features.Input.Mouse;

/// Runs an input's actions one after another, in their numbered order, with a pause between them (<c>Timing.MouseActionGap</c>), the
/// way a custom rule runs its steps. Called off the hook thread. A step that throws ends only this run: the actions call into Windows
/// and other programs, and nothing above this worker would catch it.
public static class ActionSequence
{
    /// <param name="steps">The actions in order.</param>
    /// <param name="gap">The pause between two steps.</param>
    /// <param name="gapFirst">True when the first action already started elsewhere, so the pause also comes before the first step here.</param>
    /// <param name="log">Writes a line when a step fails.</param>
    public static async Task RunAsync(IReadOnlyList<Action> steps, TimeSpan gap, bool gapFirst, Action<string> log)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if ((i > 0 || gapFirst) && gap > TimeSpan.Zero) await Task.Delay(gap);
            try
            {
                steps[i]();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                log($"action {i + 1} of a sequence failed: {ex.Message}");
                return;
            }
        }
    }
}
