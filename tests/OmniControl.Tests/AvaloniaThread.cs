using System.Collections.Concurrent;
using Avalonia;

namespace OmniControl.Tests;

/// Avalonia set up once (Win32 and Skia, no window) on one STA thread of its own, for tests that draw or decode pictures.
internal static class AvaloniaThread
{
    private static readonly BlockingCollection<Action> Work = new();

    static AvaloniaThread()
    {
        var ui = new Thread(() =>
        {
            AppBuilder.Configure<Application>().UseWin32().UseSkia().SetupWithoutStarting();
            foreach (var job in Work.GetConsumingEnumerable()) job();
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
    }

    public static T Run<T>(Func<T> job)
    {
        T? result = default;
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        Work.Add(() =>
        {
            try { result = job(); }
            catch (Exception e) { error = e; }
            done.Set();
        });
        done.Wait();
        if (error is not null) throw new InvalidOperationException("The drawing failed.", error);
        return result!;
    }
}
