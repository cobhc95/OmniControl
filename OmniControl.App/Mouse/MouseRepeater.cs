namespace OmniControl.App.Mouse;

/// <summary>Runs a step again and again while a button is held.</summary>
public static class MouseRepeater
{
    /// <summary>Runs <paramref name="step"/> after <paramref name="delay"/>, then every <paramref name="every"/>, until the returned action is called.</summary>
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
