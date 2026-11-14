using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Fx.Plugins;
using OmniControl.Core.Engine;

namespace OmniControl.App.Fx;

/// <summary>
/// One effect chain as <see cref="FxChainPanel"/> in the FX window edits it: the effects in the order the sound passes them, the
/// selection and what acts on it (FxChainViewModel.Edits.cs), and the selected
/// effect's editor: OmniControl's own cards (the equaliser, sliders, a plain settings list) or a plug-in's own editor, shown inside
/// the panel or popped out into its own window (any effect).
/// Edits go straight into <see cref="MixerService"/> settings and reach a running engine at once. Dispose when the panel goes:
/// it closes the plug-in editors it opened.
/// </summary>
public sealed partial class FxChainViewModel : ObservableObject, IDisposable, IPresetHost
{
    private readonly MixerService _mixer;
    private readonly string? _key;
    private readonly List<FxSlot> _chain;
    private readonly IEffectEditors _editors;
    private readonly FxClipboard _clipboard;
    private readonly Func<FxSlot, DynamicsReading?> _readings;

    /// <param name="presets">The user's own presets, shared by every effect window.</param>
    /// <param name="key">The program key, an input key, or null for the master.</param>
    /// <param name="editors">Where this chain's effect editors show; disposed with this view-model.</param>
    /// <param name="library">What the Add effect window lists (the program's one library).</param>
    /// <param name="readings">What the effects engine measures for one of this chain's dynamics effects now (<see cref="EffectsEngine.ReadingOf"/>).</param>
    public FxChainViewModel(MixerService mixer, string? key, IEffectEditors editors, FxClipboard clipboard, PluginLibrary library,
        Func<FxSlot, DynamicsReading?> readings, UserPresetStore presets)
    {
        Store = presets;
        _readings = readings;
        _mixer = mixer;
        Library = library;
        _key = key;
        _editors = editors;
        _clipboard = clipboard;
        _chain = mixer.FxChain(key);
        foreach (var s in _chain) Slots.Add(Track(new FxSlotViewModel(s, SaveChain, _readings, this)));
        Renumber();
        editors.Changed += RefreshEditor;
        Selected = Slots.FirstOrDefault();
    }

    public ObservableCollection<FxSlotViewModel> Slots { get; } = new();

    public UserPresetStore Store { get; }

    /// <summary>Opens the preset manager (set by the panel that shows this chain; null while none does, then nothing opens).</summary>
    public Func<PresetManagerRequest, Task>? PresetManagerOpener { get; set; }

    Task IPresetHost.ManagePresetsAsync(PresetManagerRequest request) => PresetManagerOpener?.Invoke(request) ?? Task.CompletedTask;

    /// <summary>What the Add effect window lists (built-in effects and plug-ins).</summary>
    public PluginLibrary Library { get; }

    /// <summary>The effect whose editor shows (the first of a multi-selection).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection))] private FxSlotViewModel? _selected;

    public bool HasSelection => Selected is not null;
    public bool IsEmpty => Slots.Count == 0;

    /// <summary>The selected plug-in's own editor, shown inside the panel (null: none, see <see cref="Hint"/>).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsPluginEditor))] private IPluginEditor? _pluginEditor;

    public bool ShowsPluginEditor => PluginEditor is not null;

    /// <summary>OmniControl's own cards show inside the panel: a built-in effect's editor, or a plug-in's open settings list.</summary>
    public bool ShowsCards => Selected is { } s && !_loadingEditor && !_editors.IsPoppedOut(s.Slot)
                              && (!s.IsPlugin || (s.ShowsParameters && s.PluginParameters is not null));

    public bool CanPopOut => Selected is { } s && !_editors.IsPoppedOut(s.Slot);
    public bool CanPutBack => Selected is { } s && _editors.IsPoppedOut(s.Slot);

    /// <summary>The toolbar's one Pop out / Put back control: what it does now, and its tooltip.</summary>
    public string PopText => CanPutBack ? "Put back" : "Pop out";
    public string PopTip => CanPutBack
        ? "Back into the FX window (or double-click its row)"
        : "Own window (or double-click its row)";

    private string _editorNotice = string.Empty;

    /// <summary>The sentence shown in the editor area when there is nothing to edit there (no selection, popped out, no editor, won't load).</summary>
    public string Hint => Selected is null ? "Select or add an effect to edit it." : _loadingEditor ? "Loading the plug-in…" : _editorNotice;
    public bool HasHint => Hint.Length > 0;

    private bool _loadingEditor, _disposed;

    /// <summary>The effect whose plug-in settings list the panel shows (its list is let go when another effect is shown).</summary>
    private FxSlotViewModel? _listedOn;

    partial void OnSelectedChanged(FxSlotViewModel? value)
    {
        if (_reordering) return; // a reorder briefly clears the list's selection; the editor stays open through it
        if (value is { IsPlugin: true } && !_editors.IsPoppedOut(value.Slot)) LoadEditorLater();
        else RefreshEditor();
    }

    /// <summary>
    /// Loading a plug-in and making its editor takes from a moment to seconds and must happen on the UI thread (the plug-in's editor
    /// belongs to the thread that made it). The window and the chain list show first, with "Loading the plug-in…" where the editor goes.
    /// </summary>
    private void LoadEditorLater()
    {
        _loadingEditor = true;
        PluginEditor = _editors.Embed(null, out _); // the previous editor closes now
        ReleaseList(null);
        NotifyEditorPlace();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && _loadingEditor) RefreshEditor();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Shows the selected effect's editor where it belongs: a plug-in's own editor or settings list inside the panel, a built-in
    /// effect's cards (the view shows them), or a note that it is in its own window.
    /// </summary>
    private void RefreshEditor()
    {
        _loadingEditor = false;
        var s = Selected;
        if (s is { IsPlugin: true, ShowsParameters: true } && !_editors.IsPoppedOut(s.Slot))
        {
            PluginEditor = null;
            ReleaseList(s);
            s.PluginParameters = _editors.EmbedParameters(s.Slot, out _editorNotice);
            _listedOn = s;
        }
        else
        {
            PluginEditor = _editors.Embed(s?.Slot, out _editorNotice);
            ReleaseList(null);
        }
        NotifyEditorPlace();
    }

    /// <summary>The settings list shown for an effect other than <paramref name="keep"/> was closed by the editors: that effect lets it go.</summary>
    private void ReleaseList(FxSlotViewModel? keep)
    {
        if (_listedOn is { } listed && !ReferenceEquals(listed, keep) && !_editors.IsPoppedOut(listed.Slot)) listed.PluginParameters = null;
        if (!ReferenceEquals(_listedOn, keep)) _listedOn = null;
    }

    private void NotifyEditorPlace()
    {
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasHint));
        OnPropertyChanged(nameof(ShowsCards));
        OnPropertyChanged(nameof(CanPopOut));
        OnPropertyChanged(nameof(CanPutBack));
        OnPropertyChanged(nameof(PopText));
        OnPropertyChanged(nameof(PopTip));
    }

    [RelayCommand]
    private void PopOut()
    {
        if (Selected is { } s) _editors.PopOut(s);
    }

    [RelayCommand]
    private void PutBack()
    {
        if (Selected is { } s) _editors.PutBack(s.Slot);
    }

    /// <summary>The toolbar's Pop out / Put back: the selected effect's editor into its own window, or back.</summary>
    [RelayCommand]
    private void TogglePopOutSelected()
    {
        if (Selected is { } s) TogglePopOut(s);
    }

    /// <summary>A double-click on an effect's row (TabForge's FX loader): its editor goes into its own window, or back from it.</summary>
    public void TogglePopOut(FxSlotViewModel slot)
    {
        if (_editors.IsPoppedOut(slot.Slot)) _editors.PutBack(slot.Slot);
        else _editors.PopOut(slot);
    }

    /// <summary>
    /// Parameters was pressed for an effect: a popped-out plug-in's window is made again in the new form (its own editor and its
    /// settings list are different windows); the selected effect's editor is shown again in the panel. OmniControl's own views follow by binding.
    /// </summary>
    private void ParametersChanged(FxSlotViewModel slot)
    {
        if (slot.IsPlugin && _editors.IsPoppedOut(slot.Slot))
        {
            _editors.PutBack(slot.Slot);
            _editors.PopOut(slot);
        }
        else if (ReferenceEquals(slot, Selected))
        {
            if (slot.IsPlugin) LoadEditorLater();
            else NotifyEditorPlace();
        }
    }

    /// <summary>Puts the effect chosen in the Add effect window at the end of the chain and selects it.</summary>
    public void AddEntry(PluginEntry entry)
    {
        var slot = entry.IsBuiltIn ? FxCatalog.Create(entry.Path)
            : entry.Type is { } type ? FxCatalog.CreatePlugin(type, entry.Name, entry.Path)
            : null;
        if (slot is null) return; // a plugins.json entry of a format this version does not know
        _chain.Add(slot);
        var vm = Track(new FxSlotViewModel(slot, SaveChain, _readings, this));
        Slots.Add(vm);
        Selected = vm;
        ReselectRequested?.Invoke(new[] { vm });
        Changed();
    }

    /// <summary>The effects in the order the sound passes them (the stored chain itself; read only).</summary>
    public IReadOnlyList<FxSlot> Effects => _chain;

    /// <summary>Any effect, its settings or the chain's order changed (after it was saved): the window's total response follows.</summary>
    public event Action? Edited;

    /// <summary>An effect changed: save, and the running engine takes the change at once.</summary>
    private void SaveChain()
    {
        _mixer.SaveFx(_key);
        Edited?.Invoke();
    }

    /// <summary>The chain's order or length changed: numbers, save, and the effect count on the Volume list.</summary>
    private void Changed()
    {
        Renumber();
        SaveChain();
        OnPropertyChanged(nameof(IsEmpty));
        _mixer.SetFxOn(_key, _mixer.IsFxOn(_key)); // refreshes the effect count on the Volume list
    }

    private void Renumber()
    {
        for (var i = 0; i < Slots.Count; i++) Slots[i].Number = i + 1;
    }

    private FxSlotViewModel Track(FxSlotViewModel slot)
    {
        slot.PropertyChanged += OnSlotChanged;
        return slot;
    }

    private void Untrack(FxSlotViewModel slot)
    {
        slot.PropertyChanged -= OnSlotChanged;
        slot.ReleasePresets();
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FxSlotViewModel slot) return;
        if (e.PropertyName == nameof(FxSlotViewModel.Enabled)) EnabledChanged(slot);
        else if (e.PropertyName == nameof(FxSlotViewModel.ShowsParameters)) ParametersChanged(slot);
    }

    public void Dispose()
    {
        _disposed = true;
        _editors.Changed -= RefreshEditor;
        foreach (var s in Slots) Untrack(s);
        _editors.Dispose(); // the plug-in editors close while their host window still exists
        foreach (var s in Slots) s.PluginParameters = null;
    }
}
