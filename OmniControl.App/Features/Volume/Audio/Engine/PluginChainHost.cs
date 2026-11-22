using OmniControl.App.Features.Volume.Audio.Engine.Dsp;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// The loaded plug-ins (VST3 and VST2), one per chain slot, loaded on first use on the UI thread (plug-in editors must live on the
/// thread that made the plug-in). A plug-in's settings are saved into its slot (<see cref="FxSlot.State"/>) when its editor or
/// settings list closes, every <see cref="Timing.PluginLiveSettings"/> while its own editor is open (so Equalizer APO and the FX
/// window's chart follow edits live), when the engine stops and on exit. A plug-in is unloaded only when no running chain, no editor and no settings
/// list use it, so the audio thread never meets a freed plug-in.
public sealed class PluginChainHost : IDisposable
{
    private sealed class Loaded
    {
        public IPluginEffect? Effect;
        public string? Error;
        public int Editors;
        public int Windows; // of those, the plug-in's own editor windows (not its settings list)
        public byte[]? Hash; // SHA-256 of the raw settings last put into the slot as Written
        public string? Written;
    }

    private readonly Dictionary<FxSlot, Loaded> _loaded = new(ReferenceEqualityComparer.Instance);
    private readonly Action _save;
    private readonly Func<FxSlot, (IPluginEffect? Effect, string? Error)> _load;
    /// Runs only while a plug-in's own editor is open: reads a plug-in's settings only after its change notice (<see cref="IPluginEffect.TakeChanged"/>),
    /// and encodes and saves them only when their raw bytes differ from those last kept.
    private readonly DispatcherTimer _live = new() { Interval = Timing.PluginLiveSettings };

    /// <param name="save">Writes the settings file after plug-in states were copied into their slots.</param>
    /// <param name="load">Makes a slot's plug-in; <see cref="Load"/> unless a test gives a stand-in.</param>
    public PluginChainHost(Action save, Func<FxSlot, (IPluginEffect? Effect, string? Error)>? load = null)
    {
        _save = save;
        _load = load ?? Load;
        _live.Tick += OnLiveTick;
    }

    /// A plug-in's saved settings changed (its editor or settings list closed, or states were saved): an engine that runs plug-ins from
    /// the saved settings (Equalizer APO) takes them again. UI thread.
    public event Action? StatesSaved;

    /// The plug-in's own editor opened or closed (UI thread): what shows its meters and analysers follows it.
    public event Action<FxSlot>? EditorWindowChanged;

    /// The plug-ins whose own editor is open now.
    public IReadOnlyList<FxSlot> EditorSlots() => _loaded.Where(e => e.Value.Windows > 0).Select(e => e.Key).ToList();

    /// The plug-in's own editor (not its settings list) is open now.
    public bool HasEditorWindow(FxSlot slot) => _loaded.TryGetValue(slot, out var entry) && entry.Windows > 0;

    /// The slot's plug-in, loading it (with its saved settings) the first time; or the reason it can't run.
    internal (IPluginEffect? Effect, string? Error) Get(FxSlot slot)
    {
        if (!_loaded.TryGetValue(slot, out var entry))
        {
            var (effect, error) = _load(slot);
            _loaded[slot] = entry = new Loaded { Effect = effect, Error = error };
        }
        return (entry.Effect, entry.Error);
    }

    /// A new instance of the slot's plug-in with the settings saved in the slot (the caller disposes it), or the reason it can't run.
    /// Also used on a measurement's own thread (<see cref="ChainMeasurer"/>), with a copy of the slot.
    internal static (IPluginEffect? Effect, string? Error) Load(FxSlot slot)
    {
        IPluginEffect? effect = null;
        try
        {
            var type = FxCatalog.PluginTypeOf(slot) ?? throw new InvalidOperationException("This isn't a plug-in.");
            effect = PluginLoader.Load(type.Format, slot.Path, PipeFormat.SampleRate, Core.CoreTiming.PipeMaxBlockFrames);
            if (slot.State.Length > 0) effect.LoadState(Convert.FromBase64String(slot.State));
            return (effect, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            // A plug-in that won't load (or whose saved settings it rejects) is left out of the chain and named in the status.
            effect?.Dispose();
            return (null, ex.Message);
        }
    }

    /// The slot's plug-in settings as they are now: the loaded plug-in's own (an open editor may have changed them), else the saved ones. UI thread.
    internal string StateOf(FxSlot slot)
    {
        if (!_loaded.TryGetValue(slot, out var entry) || entry.Effect is null) return slot.State;
        try
        {
            var raw = entry.Effect.SaveState();
            return Unchanged(slot, entry, raw, out _) ? slot.State : Convert.ToBase64String(raw);
        }
        catch (InvalidOperationException)
        {
            return slot.State; // the plug-in refused to hand out its settings: the last saved ones
        }
    }

    /// The raw settings are those last put into the slot (and the slot still holds them): nothing to encode or save.
    private static bool Unchanged(FxSlot slot, Loaded entry, byte[] raw, out byte[] hash)
    {
        hash = System.Security.Cryptography.SHA256.HashData(raw);
        return entry.Hash is { } last && last.AsSpan().SequenceEqual(hash) && ReferenceEquals(entry.Written, slot.State);
    }

    /// The plug-in's editor (UI thread); null with a reason when it has none or will not load. Call <see cref="EditorClosed"/> after.
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
        _loaded[slot].Windows++;
        _live.Start();
        EditorWindowChanged?.Invoke(slot);
        return editor;
    }

    /// The plug-in itself for OmniControl's own list of its settings (UI thread); null with a reason when it will not load or has no
    /// setting a host may change. Held loaded like an open editor: call <see cref="EditorClosed"/> after.
    public IPluginEffect? OpenParameters(FxSlot slot, out string? error)
    {
        var (effect, loadError) = Get(slot);
        error = loadError;
        if (effect is null) return null;
        if (effect.Parameters.Count == 0) { error = "This plug-in has no settings a host can change."; return null; }
        _loaded[slot].Editors++;
        return effect;
    }

    /// The editor or settings list was closed: keep what the user changed. <paramref name="wasEditor"/>: it was the plug-in's own editor.
    public void EditorClosed(FxSlot slot, bool wasEditor)
    {
        if (_loaded.TryGetValue(slot, out var entry))
        {
            entry.Editors = Math.Max(0, entry.Editors - 1);
            if (wasEditor) entry.Windows = Math.Max(0, entry.Windows - 1);
        }
        if (!_loaded.Values.Any(e => e.Windows > 0)) _live.Stop();
        if (wasEditor) EditorWindowChanged?.Invoke(slot); // before anything is unloaded: a feed stops using the plug-in first
        SaveStates();
    }

    private void OnLiveTick(object? sender, EventArgs e) =>
        Guard.Run("Plug-ins: live settings", () => SaveStates(_loaded.Where(l => l.Value.Windows > 0 && l.Value.Effect?.TakeChanged() == true).ToList()));

    /// Copies every loaded plug-in's settings into its slot and saves.
    public void SaveStates() => SaveStates(_loaded);

    private void SaveStates(IEnumerable<KeyValuePair<FxSlot, Loaded>> loaded)
    {
        var changed = false;
        foreach (var (slot, entry) in loaded)
        {
            if (entry.Effect is null) continue;
            try
            {
                var raw = entry.Effect.SaveState();
                if (Unchanged(slot, entry, raw, out var hash)) continue;
                var state = Convert.ToBase64String(raw);
                if (state != slot.State) { slot.State = state; changed = true; }
                entry.Hash = hash;
                entry.Written = slot.State;
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

    /// Unloads plug-ins that no running chain and no open editor use (call after a chain stopped). Saves their settings first.
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
        _live.Stop();
        _live.Tick -= OnLiveTick;
        SaveStates();
        foreach (var entry in _loaded.Values) entry.Effect?.Dispose();
        _loaded.Clear();
    }
}
