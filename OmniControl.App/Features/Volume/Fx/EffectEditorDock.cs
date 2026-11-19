using Avalonia.Data;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Fx;

/// The effect editors of one FX window (the idea of TabForge's <c>FxEditorController</c> and <c>FloatingEditors</c>, same owner):
/// what is shown inside the window for the selected plug-in (its own editor or its settings list), and the effects popped out into
/// their own windows (<see cref="EffectEditorWindow"/>, above the FX window through <see cref="OwnedWindow"/>), remembered until they
/// or the FX window close. A plug-in's own editor or settings list is opened and closed in pairs through <see cref="PluginChainHost"/>,
/// so a plug-in is never unloaded under either. UI thread only.
internal sealed class EffectEditorDock : IEffectEditors
{
    private const string PoppedOutNotice = "This effect's window is open on its own. Put back shows it here.";

    private readonly PluginChainHost _plugins;
    private readonly Func<Window?> _owner;
    private readonly Dictionary<FxSlot, EffectEditorWindow> _poppedOut = new(ReferenceEqualityComparer.Instance);
    private readonly Action<string> _report;
    private FxSlot? _embeddedSlot;
    private IPluginEditor? _embedded;
    private PluginParameterList? _embeddedParameters;
    private string _embeddedNotice = string.Empty;
    private bool _disposed;

    /// <param name="owner">The FX window the popped-out editors stay above.</param>
    /// <param name="report">Shows a message in the status bar when a popped-out editor can't open.</param>
    public EffectEditorDock(PluginChainHost plugins, Func<Window?> owner, Action<string> report)
    {
        _plugins = plugins;
        _owner = owner;
        _report = report;
    }

    public event Action? Changed;

    public bool IsPoppedOut(FxSlot slot) => _poppedOut.ContainsKey(slot);

    public IPluginEditor? Embed(FxSlot? slot, out string notice)
    {
        if (Shown(slot, out notice) is not { } plugin) return null;
        if (!ReferenceEquals(plugin, _embeddedSlot) || _embeddedParameters is not null)
        {
            CloseEmbedded();
            _embeddedSlot = plugin;
            _embedded = _plugins.OpenEditor(plugin, out var error);
            _embeddedNotice = _embedded is null ? error ?? string.Empty : string.Empty;
        }
        notice = _embeddedNotice;
        return _embedded;
    }

    public PluginParameterList? EmbedParameters(FxSlot slot, out string notice)
    {
        if (Shown(slot, out notice) is not { } plugin) return null;
        if (!ReferenceEquals(plugin, _embeddedSlot) || _embeddedParameters is null)
        {
            CloseEmbedded();
            _embeddedSlot = plugin;
            _embeddedParameters = OpenParameters(plugin, out var error);
            _embeddedNotice = _embeddedParameters is null ? error ?? string.Empty : string.Empty;
        }
        notice = _embeddedNotice;
        return _embeddedParameters;
    }

    /// The plug-in to show inside the window: null (what was shown closes) for no slot, a built-in effect or a popped-out effect.
    private FxSlot? Shown(FxSlot? slot, out string notice)
    {
        notice = string.Empty;
        if (_disposed || slot is null) { CloseEmbedded(); return null; }
        if (IsPoppedOut(slot)) { CloseEmbedded(); notice = PoppedOutNotice; return null; }
        if (FxCatalog.IsPlugin(slot)) return slot;
        CloseEmbedded();
        return null;
    }

    private PluginParameterList? OpenParameters(FxSlot slot, out string? error) =>
        _plugins.OpenParameters(slot, out error) is { } effect ? new PluginParameterList(effect) : null;

    private void CloseEmbedded()
    {
        if (_embeddedSlot is { } slot && (_embedded is not null || _embeddedParameters is not null))
        {
            var wasEditor = _embedded is not null;
            _embedded?.Dispose(); // removed from its host window, which still exists
            _embeddedParameters?.Close();
            _plugins.EditorClosed(slot, wasEditor);
        }
        _embeddedSlot = null;
        _embedded = null;
        _embeddedParameters = null;
        _embeddedNotice = string.Empty;
    }

    public void PopOut(FxSlotViewModel effect)
    {
        var slot = effect.Slot;
        if (_disposed) return;
        if (_poppedOut.TryGetValue(slot, out var open)) { open.Activate(); return; }
        if (ReferenceEquals(slot, _embeddedSlot)) CloseEmbedded();
        var window = !effect.IsPlugin ? OwnEditorWindow(effect)
            : effect.ShowsParameters ? ParametersWindow(effect)
            : PluginWindow(effect);
        if (window is null)
        {
            Changed?.Invoke();
            return;
        }
        _poppedOut[slot] = window;
        window.Closed += (_, _) =>
        {
            _poppedOut.Remove(slot);
            if (!_disposed) Changed?.Invoke(); // closing the window puts the editor back
        };
        OwnedWindow.Show(window, _owner());
        Changed?.Invoke();
    }

    /// A built-in effect: the same editor view as inside the FX window, sized for the effect's scale.
    private static EffectEditorWindow OwnEditorWindow(FxSlotViewModel effect) =>
        new(effect.Name, new EffectEditorView { DataContext = effect, Margin = new Thickness(EditorFit.PopOutPadding) }, canResize: true,
            EditorFit.OwnEditorWidth * effect.ScaleFactor, EditorFit.OwnEditorHeight * effect.ScaleFactor);

    /// A plug-in shown as its settings list: the plug-in is held open for the window and its list goes on the effect.
    private EffectEditorWindow? ParametersWindow(FxSlotViewModel effect)
    {
        if (OpenParameters(effect.Slot, out var error) is not { } list)
        {
            _report($"{effect.Name}: {error}");
            return null;
        }
        effect.PluginParameters = list;
        var window = OwnEditorWindow(effect);
        window.Closed += (_, _) =>
        {
            list.Close();
            if (ReferenceEquals(effect.PluginParameters, list)) effect.PluginParameters = null;
            _plugins.EditorClosed(effect.Slot, wasEditor: false);
        };
        return window;
    }

    /// A plug-in's own editor: the window takes the editor's size within the screen, at the effect's scale.
    private EffectEditorWindow? PluginWindow(FxSlotViewModel effect)
    {
        if (_plugins.OpenEditor(effect.Slot, out var error) is not { } editor)
        {
            _report($"{effect.Name}: {error}");
            return null;
        }
        var area = new PluginEditorArea { Sizing = EditorSizing.FitWindow, Editor = editor };
        area.Bind(PluginEditorArea.UserScaleProperty, new Binding(nameof(FxSlotViewModel.ScaleFactor)) { Source = effect });
        area.ScaleRefused += effect.PluginScaleRefused;
        var window = new EffectEditorWindow(effect.Name, area, editor.CanResize, EditorFit.MinPopOutWidth, EditorFit.MinPopOutHeight);
        window.Closing += (_, _) => editor.Dispose(); // before the window's native handle goes
        window.Closed += (_, _) =>
        {
            area.ScaleRefused -= effect.PluginScaleRefused;
            _plugins.EditorClosed(effect.Slot, wasEditor: true);
        };
        return window;
    }

    public void PutBack(FxSlot slot)
    {
        if (_poppedOut.TryGetValue(slot, out var open)) open.Close();
    }

    public void Leaving(IEnumerable<FxSlot> slots)
    {
        foreach (var slot in slots.ToList())
        {
            if (ReferenceEquals(slot, _embeddedSlot)) CloseEmbedded();
            PutBack(slot);
        }
    }

    /// The FX window is closing: every editor closes while its window still exists.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseEmbedded();
        foreach (var window in _poppedOut.Values.ToList()) window.Close();
    }
}
