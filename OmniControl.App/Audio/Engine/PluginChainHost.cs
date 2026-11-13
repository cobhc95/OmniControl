using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// The loaded plug-ins (VST3 and VST2), one per chain slot, loaded on first use on the UI thread (plug-in editors must live on the
/// thread that made the plug-in). A plug-in's settings are saved into its slot (<see cref="FxSlot.State"/>) when its editor or
/// settings list closes, when the engine stops and on exit. A plug-in is unloaded only when no running chain, no editor and no settings
/// list use it, so the audio thread never meets a freed plug-in.
/// </summary>
public sealed class PluginChainHost : IDisposable
{
    private sealed class Loaded
    {
        public IPluginEffect? Effect;
        public string? Error;
        public int Editors;
    }

    private readonly Dictionary<FxSlot, Loaded> _loaded = new(ReferenceEqualityComparer.Instance);
    private readonly Action _save;

    /// <param name="save">Writes the settings file after plug-in states were copied into their slots.</param>
    public PluginChainHost(Action save) => _save = save;

    /// <summary>
    /// A plug-in's saved settings changed (its editor or settings list closed, or states were saved): an engine that runs plug-ins from
    /// the saved settings (Equalizer APO) takes them again. UI thread.
    /// </summary>
    public event Action? StatesSaved;

    /// <summary>The slot's plug-in, loading it (with its saved settings) the first time; or the reason it can't run.</summary>
    internal (IPluginEffect? Effect, string? Error) Get(FxSlot slot)
    {
        if (!_loaded.TryGetValue(slot, out var entry))
        {
            entry = new Loaded();
            try
            {
                var type = FxCatalog.PluginTypeOf(slot) ?? throw new InvalidOperationException("This isn't a plug-in.");
                entry.Effect = PluginLoader.Load(type.Format, slot.Path, PipeFormat.SampleRate, Core.CoreTiming.PipeMaxBlockFrames);
                if (slot.State.Length > 0) entry.Effect.LoadState(Convert.FromBase64String(slot.State));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                // A plug-in that won't load (or whose saved settings it rejects) is left out of the chain and named in the status.
                entry.Effect?.Dispose();
                entry.Effect = null;
                entry.Error = ex.Message;
            }
            _loaded[slot] = entry;
        }
        return (entry.Effect, entry.Error);
    }

    /// <summary>The plug-in's editor (UI thread); null with a reason when it has none or will not load. Call <see cref="EditorClosed"/> after.</summary>
    public IPluginEditor? OpenEditor(FxSlot slot, out string? error)
    {
        var (effect, loadError) = Get(slot);
        error = loadError;
        if (effect is null) return null;
        IPluginEditor? editor;
        try
        {
            editor = effect.CreateEditor();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException)
        {
            // The FX window opens editors as soon as an effect is selected: a plug-in that fails here must only say so.
            error = $"Couldn't open the plug-in's window: {ex.Message}";
            return null;
        }
        if (editor is null) { error = "This plug-in has no editor window."; return null; }
        _loaded[slot].Editors++;
        return editor;
    }

    /// <summary>
    /// The plug-in itself for OmniControl's own list of its settings (UI thread); null with a reason when it will not load or has no
    /// setting a host may change. Held loaded like an open editor: call <see cref="EditorClosed"/> after.
    /// </summary>
    public IPluginEffect? OpenParameters(FxSlot slot, out string? error)
    {
        var (effect, loadError) = Get(slot);
        error = loadError;
        if (effect is null) return null;
        if (effect.Parameters.Count == 0) { error = "This plug-in has no settings a host can change."; return null; }
        _loaded[slot].Editors++;
        return effect;
    }

    /// <summary>The editor or settings list was closed: keep what the user changed.</summary>
    public void EditorClosed(FxSlot slot)
    {
        if (_loaded.TryGetValue(slot, out var entry)) entry.Editors = Math.Max(0, entry.Editors - 1);
        SaveStates();
    }

    /// <summary>Copies every loaded plug-in's settings into its slot and saves.</summary>
    public void SaveStates()
    {
        var changed = false;
        foreach (var (slot, entry) in _loaded)
        {
            if (entry.Effect is null) continue;
            try
            {
                var state = Convert.ToBase64String(entry.Effect.SaveState());
                if (state != slot.State) { slot.State = state; changed = true; }
            }
            catch (InvalidOperationException)
            {
                // The plug-in refused to hand out its settings: keep the last saved ones.
            }
        }
        if (!changed) return;
        _save();
        StatesSaved?.Invoke();
    }

    /// <summary>Unloads plug-ins that no running chain and no open editor use (call after a chain stopped). Saves their settings first.</summary>
    internal void ReleaseUnused(IReadOnlySet<FxSlot> inUse)
    {
        SaveStates();
        foreach (var slot in _loaded.Keys.Where(s => !inUse.Contains(s) && _loaded[s].Editors == 0).ToList())
        {
            _loaded[slot].Effect?.Dispose();
            _loaded.Remove(slot);
        }
    }

    public void Dispose()
    {
        SaveStates();
        foreach (var entry in _loaded.Values) entry.Effect?.Dispose();
        _loaded.Clear();
    }
}
