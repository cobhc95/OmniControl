using Avalonia.Headless;
using OmniControl.App;

namespace OmniControl.Screenshots;

/// Draws the README screenshots off screen: Avalonia's headless platform with Skia drawing, the app's own styles (App.axaml), its real
/// views and view-models fed with sample data. Nothing is shown and nothing on the PC is changed: settings live in a temp folder
/// (<see cref="Sandbox"/>), no action is ever run (<see cref="NoRunner"/>), the master list's keys are never registered, and no mouse
/// hook, device read, resource poll, audio stream, effects engine or network link is started; what still listens or tries is said where it
/// is made (the OSD's listeners, the Automation service's pause key, which fails while OmniControl runs). Each shot's notes say what it
/// fakes. Usage: dotnet run -c Debug [-- output-folder]; default docs/screenshots.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : RepoScreenshots();
        Directory.CreateDirectory(output);
        using var sandbox = Sandbox.Enter();
        AppBuilder.Configure<App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        AppSettings.Current.Theme = ThemeChoice.Dark; // App settings applies the saved theme when it is made

        var camera = new Camera(output);
        var rig = new Rig();
        var failed = new List<string>();
        foreach (var (name, shoot) in Shots.All(rig, camera))
        {
            try
            {
                shoot();
                Console.WriteLine($"ok    {name}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failed.Add(name);
                Console.WriteLine($"FAIL  {name}: {ex}");
            }
        }
        foreach (var (file, bytes) in camera.Saved) Console.WriteLine($"{file,-28}{bytes / 1024.0,8:0.0} KB");
        Console.WriteLine($"{"Total",-28}{camera.Saved.Sum(s => s.Bytes) / 1024.0,8:0.0} KB");
        sandbox.CheckRealDataUntouched();
        Console.WriteLine(failed.Count == 0 ? "All shots drawn." : $"Not drawn: {string.Join(", ", failed)}");
        return failed.Count;
    }

    private static string RepoScreenshots()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OmniControl.slnx"))) dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Run from inside the repository, or pass the output folder.");
        return Path.Combine(dir.FullName, "docs", "screenshots");
    }
}
