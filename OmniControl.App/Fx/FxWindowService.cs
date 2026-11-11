using OmniControl.App.Audio;

namespace OmniControl.App.Fx;

/// <summary>Opens FX chain windows, one per target (the master or a program), and brings an already-open one to the front.</summary>
public sealed class FxWindowService
{
    private readonly MixerService _mixer;
    private readonly Dictionary<string, FxWindow> _open = new();

    private readonly Action<string> _report;

    /// <param name="report">Shows a message in the status bar when a window can't be opened.</param>
    public FxWindowService(MixerService mixer, Action<string> report)
    {
        _mixer = mixer;
        _report = report;
    }

    /// <param name="key">The program key, or null for the master.</param>
    public void Open(string? key)
    {
        var id = key ?? "master";
        if (_open.TryGetValue(id, out var existing)) { existing.Activate(); return; }

        try
        {
            var window = new FxWindow { DataContext = new FxChainViewModel(_mixer, key) };
            window.Closed += (_, _) => _open.Remove(id);
            _open[id] = window;
            window.Show();
        }
        catch (Exception ex)
        {
            _open.Remove(id);
            _report($"Couldn't open the FX window: {ex.Message}"); // a window problem must never take the program down
        }
    }

    public void CloseAll()
    {
        foreach (var w in _open.Values.ToList()) w.Close();
    }
}
