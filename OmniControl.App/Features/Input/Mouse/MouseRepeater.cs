namespace OmniControl.App.Features.Input.Mouse;

/// Runs a step again and again while a button is held.
public static class MouseRepeater
{
    /// Runs <paramref name="step"/> after <paramref name="delay"/>, then every <paramref name="every"/>, until the returned action is called.
    public static Action Start(Action step, TimeSpan delay, TimeSpan every)
    {
        var stop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, stop.Token);
                while (true)
                {
                    step();
                    await Task.Delay(every, stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // The button was released: that is how repeating ends.
            }
        });
        return stop.Cancel;
    }
}
