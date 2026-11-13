using Avalonia.Controls;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Fx.Plugins;
using OmniControl.App.Services;

namespace OmniControl.App.Fx;

/// <summary>
/// Opens the effects windows: <see cref="Open"/> shows one FX window per target (the master, a program, a recording device or a
/// combined device) around its chain's <see cref="FxChainPanel"/>; <see cref="AskAsync"/> shows effects-setup messages. Every window is shown through
/// <see cref="OwnedWindow"/>, so it stays above the main window. An already-open FX window is brought to the front.
/// </summary>
public sealed class FxWindowService
{
    private readonly MixerService _mixer;
    private readonly EffectsEngine _engine;
    private readonly SoundModeService _modes;
    private readonly PluginChainHost _plugins;
    private readonly FxClipboard _clipboard;
    private readonly PluginLibrary _library;
    private readonly Dictionary<string, FxWindow> _open = new();
    private readonly Action<string> _report;

    /// <param name="report">Shows a message in the status bar when a window or a plug-in editor can't be opened.</param>
    /// <param name="library">The plug-ins the Add effect window offers (shared with the App settings page's scan).</param>
    public FxWindowService(MixerService mixer, EffectsEngine engine, SoundModeService modes, PluginChainHost plugins, PluginLibrary library,
        Action<string> report)
    {
        _library = library;
        _mixer = mixer;
        _engine = engine;
        _modes = modes;
        _plugins = plugins;
        _report = report;
        _clipboard = new FxClipboard(plugins.SaveStates);
    }

    /// <summary>The view-model of one chain; its popped-out effect editors stay above <paramref name="owner"/>, the FX window.</summary>
    private FxChainViewModel CreateChain(string? key, Func<Window?> owner) =>
        new(_mixer, key, new EffectEditorDock(_plugins, owner, _report), _clipboard, _library);

    /// <param name="key">The chain's key (a program, an input or a combined device), or null for the master.</param>
    public void Open(string? key)
    {
        var id = key ?? MixerService.MasterKey;
        if (_open.TryGetValue(id, out var existing)) { existing.Activate(); return; }

        FxWindow? window = null;
        FxWindowViewModel? vm = null;
        try
        {
            vm = new FxWindowViewModel(_mixer, _engine, _modes, key, CreateChain(key, () => window));
            window = new FxWindow { DataContext = vm };
            // Plug-in editors are taken out while the window still exists (both plug-in formats ask for that), so this is Closing, not Closed.
            window.Closing += (_, _) => vm.Dispose();
            window.Closed += (_, _) => _open.Remove(id);
            _open[id] = window;
            OwnedWindow.Show(window);
        }
        catch (Exception ex)
        {
            _open.Remove(id);
            vm?.Dispose();
            _report($"Couldn't open the FX window: {ex.Message}"); // a window problem must never take the program down
        }
    }

    /// <summary>A message window for effects setup; true when the user confirmed.</summary>
    public Task<bool> AskAsync(EngineMessage message) => new MessageWindow(message).AskAsync();

    /// <summary>Closes every FX window (and with them their plug-in editors) before the plug-ins are unloaded at exit.</summary>
    public void CloseAll()
    {
        foreach (var w in _open.Values.ToList()) w.Close();
    }
}
