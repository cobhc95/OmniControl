using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.Shell;

namespace OmniControl.App.Showcase;

/// <summary>
/// The two hidden ways in to <see cref="ShowcaseExporter"/>: the <c>--export-screenshots &lt;folder&gt;</c> run (hidden window, throw-away
/// data, exits when done) and the Ctrl+Alt+Shift+F12 chord in a running Debug build (the owner's own data, with personal names covered).
/// </summary>
internal static class ShowcaseRun
{
    private const Key ChordKey = Key.F12;
    private const KeyModifiers ChordModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift;

    /// <summary>
    /// Starts the export when the command line asks for it: the window stays off screen, nothing of the normal start-up (tray, start-up
    /// entry) runs, and the process ends with exit code 0 (1 when it failed; the reason goes to <c>export-error.txt</c> in the folder).
    /// </summary>
    /// <returns>True when this run is an export run (the caller then skips the normal start-up).</returns>
    public static bool TryStart(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, ShellViewModel shell)
    {
        if (ShowcaseOptions.ExportFolder(desktop.Args) is not { } folder) return false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(-32000, -32000);
        window.ShowInTaskbar = false;
        var data = Environment.GetEnvironmentVariable(ShowcaseOptions.DataFolderVariable);
        desktop.Exit += (_, _) =>
        {
            shell.Dispose();
            TryDelete(data);
        };
        window.Opened += async (_, _) =>
        {
            var code = 0;
            try
            {
                Directory.CreateDirectory(folder);
                await new ShowcaseExporter(window, shell, folder, isolated: data is not null, keepNames: OmniControl.App.Services.StartupMode.ReadOnly).RunAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                code = 1; // a developer tool: say why in the folder and end, rather than hang a hidden window
                File.WriteAllText(Path.Combine(folder, "export-error.txt"), ex.ToString());
            }
            desktop.Shutdown(code);
        };
        return true;
    }

    /// <summary>Gives <paramref name="window"/> the Ctrl+Alt+Shift+F12 chord.</summary>
    public static void AttachChord(MainWindow window)
    {
        var running = false;
        window.AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            if (e.Key != ChordKey || e.KeyModifiers != ChordModifiers || running || window.DataContext is not ShellViewModel shell) return;
            e.Handled = true;
            running = true;
            try
            {
                var folder = ShowcaseOptions.ChordFolder();
                Directory.CreateDirectory(folder);
                var count = await new ShowcaseExporter(window, shell, folder, isolated: false).RunAsync();
                ((IShowcaseSource)shell).ShowcaseSay($"Showcase: {count} pictures saved to {folder}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                ((IShowcaseSource)shell).ShowcaseSay($"Showcase export failed: {ex.Message}");
            }
            finally { running = false; }
        }, RoutingStrategies.Tunnel);
    }

    private static void TryDelete(string? folder)
    {
        if (folder is null) return;
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { } // a temporary folder: Windows clears it in time
        catch (UnauthorizedAccessException) { }
    }
}
