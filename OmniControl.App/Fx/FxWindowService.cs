using Avalonia.Controls;
using OmniControl.App.Dialogs;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Fx.Plugins;
using OmniControl.App.Fx.Presets;
using OmniControl.App.Presets;
using OmniControl.App.Services;

namespace OmniControl.App.Fx;

/// <summary>
/// Opens the effects windows: <see cref="Open"/> shows one FX window per target (the master, a program, a recording device or a
/// combined device) around its chain's <see cref="FxChainPanel"/>. Every window is shown through
/// <see cref="OwnedWindow"/>, so it stays above the main window. An already-open FX window is brought to the front. It also makes the
/// effects' preset groups of the preset manager (<see cref="PresetGroupsFor"/>), since it knows which effect a preset reaches.
/// </summary>
public sealed class FxWindowService
{
    private readonly MixerService _mixer;
    private readonly EffectsEngine _engine;
    private readonly EffectsSupportService _support;
    private readonly PluginChainHost _plugins;
    private readonly FxClipboard _clipboard;
    private readonly PluginLibrary _library;
    private readonly UserPresetStore _presets;
    private readonly Dictionary<string, FxWindow> _open = new();
    private readonly Action<string> _report;
    private readonly Action<string?, string?> _openPresets;
    private string? _lastActive; // the FX window (its chain's id) the user worked in last

    /// <param name="report">Shows a message in the status bar when a window or a plug-in editor can't be opened.</param>
    /// <param name="presets">The user's own presets (the preset pull-down of every FX window and the preset manager's effect cards).</param>
    /// <param name="library">The plug-ins the Add effect window offers (shared with the App settings page's scan).</param>
    /// <param name="openPresets">Opens the shared preset manager at a section and group (a preset box's "Manage presets...").</param>
    public FxWindowService(MixerService mixer, EffectsEngine engine, EffectsSupportService support, PluginChainHost plugins, PluginLibrary library, UserPresetStore presets,
        Action<string> report, Action<string?, string?> openPresets)
    {
        _openPresets = openPresets;
        _library = library;
        _presets = presets;
        _mixer = mixer;
        _engine = engine;
        _support = support;
        _plugins = plugins;
        _report = report;
        _clipboard = new FxClipboard(plugins.SaveStates);
    }

    /// <summary>The view-model of one chain; its popped-out effect editors stay above <paramref name="owner"/>, the FX window.</summary>
    private FxChainViewModel CreateChain(string? key, Func<Window?> owner) =>
        new(_mixer, key, new EffectEditorDock(_plugins, owner, _report), _clipboard, _library, slot => _engine.ReadingOf(key, slot), _presets,
            kind => _openPresets(null, FxKindPresets.GroupId(kind)));

    /// <summary>One preset group per effect kind that has presets, in the catalog's order, for the preset manager's <paramref name="section"/> (the Volume page).</summary>
    public IEnumerable<PresetGroup> PresetGroupsFor(string section) =>
        PresetCatalog.Kinds.Select(kind => new PresetGroup(FxKindPresets.GroupId(kind), section, kind.Name, new FxKindPresets(kind, _presets, PresetTargetOf)));

    /// <summary>
    /// The effect a preset of this kind is read from and applied to: the selected effect of the FX window used last when it is of this
    /// kind; else the master's first effect of the kind (through the master's FX window when it is open, so its editor shows the change);
    /// null when the master has none.
    /// </summary>
    private IFxPresetTarget? PresetTargetOf(FxKind kind)
    {
        if (_lastActive is { } last && ChainOf(last)?.PresetTargetOf(kind, selectedOnly: true) is { } selected) return selected;
        if (ChainOf(MixerService.MasterKey) is { } master) return master.PresetTargetOf(kind, selectedOnly: false);
        return _mixer.FxChain(null).FirstOrDefault(s => s.Kind == kind.Kind) is { } slot
            ? new SlotPresetTarget(kind, slot, _presets, () => _mixer.SaveFx(null))
            : null;
    }

    private FxChainViewModel? ChainOf(string id) => _open.TryGetValue(id, out var window) && window.DataContext is FxWindowViewModel vm ? vm.Chain : null;

    /// <param name="key">The chain's key (a program, an input or a combined device), or null for the master.</param>
    public void Open(string? key)
    {
        var id = key ?? MixerService.MasterKey;
        if (_open.TryGetValue(id, out var existing)) { existing.Activate(); return; }

        FxWindow? window = null;
        FxWindowViewModel? vm = null;
        try
        {
            vm = new FxWindowViewModel(_mixer, _engine, _support, key, CreateChain(key, () => window));
            window = new FxWindow { DataContext = vm };
            // Plug-in editors are taken out while the window still exists (both plug-in formats ask for that), so this is Closing, not Closed.
            window.Closing += (_, _) => vm.Dispose();
            window.Activated += (_, _) => _lastActive = id;
            window.Closed += (_, _) =>
            {
                _open.Remove(id);
                if (_lastActive == id) _lastActive = null;
            };
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

    /// <summary>The Mixer row's FX button: closes the target's FX window when it is open, else opens it (<see cref="Open"/>).</summary>
    /// <param name="key">The chain's key, or null for the master.</param>
    public void Toggle(string? key)
    {
        if (_open.TryGetValue(key ?? MixerService.MasterKey, out var open)) open.Close();
        else Open(key);
    }

    /// <summary>Closes every FX window (and with them their plug-in editors) before the plug-ins are unloaded at exit.</summary>
    public void CloseAll()
    {
        foreach (var w in _open.Values.ToList()) w.Close();
    }
}
