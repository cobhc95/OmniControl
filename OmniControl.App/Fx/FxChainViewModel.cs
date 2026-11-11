using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Fx.Plugins;
using OmniControl.App.Pages;
using OmniControl.App.Services;

namespace OmniControl.App.Fx;

/// <summary>
/// The FX chain window for the master (key null) or one program: the ordered effects, the selected effect's editor,
/// and the program's output device. Edits go straight into <see cref="MixerService"/> settings.
/// </summary>
public sealed partial class FxChainViewModel : ObservableObject, IDisposable
{
    private readonly MixerService _mixer;
    private readonly string? _key;
    private readonly List<FxSlot> _chain;

    public FxChainViewModel(MixerService mixer, string? key)
    {
        _mixer = mixer;
        _key = key;
        _chain = mixer.FxChain(key);

        var entry = key is null ? null : mixer.Find(key);
        Title = "FX: " + (key is null ? "Master" : MixerService.IsInput(key) ? mixer.InputName(key) : entry?.Name ?? "Program");
        foreach (var s in _chain) Slots.Add(new FxSlotViewModel(s, mixer.Save));
        Selected = Slots.FirstOrDefault();
        if (entry is not null) Output = BuildOutput(entry);

        mixer.Updated += OnMixerUpdated;
    }

    public string Title { get; }
    public bool IsProgram => _key is not null && !MixerService.IsInput(_key);
    public ObservableCollection<FxSlotViewModel> Slots { get; } = new();

    /// <summary>What the Add effect window lists (built-in effects and VST3 plug-ins).</summary>
    public PluginLibrary Library { get; } = new();

    /// <summary>The program's output device picker (null for the master).</summary>
    public ChoiceItem? Output { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection), nameof(Editor))] private FxSlotViewModel? _selected;

    public bool HasSelection => Selected is not null;
    public bool IsEmpty => Slots.Count == 0;
    public IReadOnlyList<object>? Editor => Selected?.EditorItems;

    /// <summary>The sentence shown in the editor area when there are no cards to show.</summary>
    public string Hint => Selected is null ? "Select or add an effect to edit it." : Selected.Hint;
    public bool HasHint => Hint.Length > 0;

    partial void OnSelectedChanged(FxSlotViewModel? value)
    {
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasHint));
    }

    /// <summary>The whole chain is switched on (the green power button on the Volume list).</summary>
    public bool FxOn
    {
        get => _mixer.IsFxOn(_key);
        set { if (_mixer.IsFxOn(_key) != value) _mixer.SetFxOn(_key, value); }
    }

    /// <summary>A remembered program that isn't running can be forgotten from here.</summary>
    public bool CanForget => IsProgram && !_mixer.Running.Any(r => r.Key == _key && r.IsOpen);

    public string EffectCount => Slots.Count == 1 ? "1 effect" : $"{Slots.Count} effects";

    /// <summary>Option: FX windows stay above other windows. Applies to every FX window as it opens.</summary>
    public bool KeepOnTop
    {
        get => AppSettings.Current.FxKeepOnTop;
        set { AppSettings.Current.FxKeepOnTop = value; AppSettings.Current.Save(); OnPropertyChanged(); }
    }

    public event Action? CloseRequested;

    [RelayCommand] private void CloseWindow() => CloseRequested?.Invoke();

    private void OnMixerUpdated()
    {
        OnPropertyChanged(nameof(FxOn));
        OnPropertyChanged(nameof(CanForget));
    }

    private ChoiceItem BuildOutput(AppAudioEntry entry)
    {
        const string def = "Default device";
        var options = new List<string> { def };
        options.AddRange(_mixer.Devices.Select(d => d.Name));
        var current = def;
        if (entry.DeviceId.Length > 0)
        {
            current = _mixer.Devices.FirstOrDefault(d => d.Id == entry.DeviceId)?.Name ?? $"{entry.DeviceName} (unavailable)";
            if (!options.Contains(current)) options.Add(current);
        }
        return new ChoiceItem("Output", options, current, name =>
        {
            if (string.IsNullOrEmpty(name)) return;
            if (name == def) _mixer.SetDevice(_key!, string.Empty, string.Empty);
            else if (_mixer.Devices.FirstOrDefault(d => d.Name == name) is { } d) _mixer.SetDevice(_key!, d.Id, d.Name);
            // choosing the "(unavailable)" entry keeps the remembered device
        });
    }

    /// <summary>Puts the effect chosen in the Add effect window at the end of the chain and selects it.</summary>
    public void AddEntry(PluginEntry entry)
    {
        var slot = entry.IsBuiltIn ? FxCatalog.Create(entry.Path) : FxCatalog.CreatePlugin(entry.Name, entry.Path);
        _chain.Add(slot);
        var vm = new FxSlotViewModel(slot, _mixer.Save);
        Slots.Add(vm);
        Selected = vm;
        Changed();
    }

    private List<FxSlotViewModel> _picked = new();

    /// <summary>Raised when the view should select these effects again (after a move rebuilt the selection).</summary>
    public event Action<IReadOnlyList<FxSlotViewModel>>? ReselectRequested;

    /// <summary>The effects highlighted in the list (Ctrl+click, Shift+click or arrows, Ctrl+A). The editor shows the first.</summary>
    public void SetPicked(IEnumerable<FxSlotViewModel> picked) => _picked = picked.ToList();

    private List<FxSlotViewModel> Targets() => _picked.Count > 0 ? _picked.ToList() : Selected is { } s ? new() { s } : new();

    /// <summary>Space: the highlighted effects go on or off together (all off if the first is on).</summary>
    public void ToggleEnabled()
    {
        var t = Targets();
        if (t.Count == 0) return;
        var to = !t[0].Enabled;
        foreach (var s in t) s.Enabled = to;
    }

    [RelayCommand]
    private void Remove()
    {
        var targets = Targets();
        if (targets.Count == 0) return;
        var at = targets.Min(t => Slots.IndexOf(t));
        foreach (var s in targets) { _chain.Remove(s.Slot); Slots.Remove(s); }
        _picked.Clear();
        Selected = Slots.Count == 0 ? null : Slots[Math.Min(at, Slots.Count - 1)];
        Changed();
    }

    [RelayCommand] private void MoveUp() => Move(-1);
    [RelayCommand] private void MoveDown() => Move(1);

    /// <summary>Moves the highlighted effects one place up or down, keeping their order; effects at the edge stay put.</summary>
    private void Move(int delta)
    {
        var targets = Targets();
        if (targets.Count == 0) return;
        var order = delta < 0 ? targets.OrderBy(Slots.IndexOf) : targets.OrderByDescending(Slots.IndexOf);
        foreach (var s in order)
        {
            var from = Slots.IndexOf(s);
            var to = from + delta;
            if (to < 0 || to >= Slots.Count || targets.Contains(Slots[to])) continue;
            _chain.RemoveAt(from);
            _chain.Insert(to, s.Slot);
            Slots.Move(from, to);
        }
        Changed();
        ReselectRequested?.Invoke(targets);
    }

    /// <summary>Moves the effect at <paramref name="from"/> to position <paramref name="to"/> (drag and drop, Alt+Up/Down).</summary>
    public void MoveTo(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Slots.Count || to >= Slots.Count) return;
        var selected = Selected;
        var slot = _chain[from];
        _chain.RemoveAt(from);
        _chain.Insert(to, slot);
        Slots.Move(from, to);
        Selected = selected;
        Changed();
    }

    [RelayCommand]
    private void Forget()
    {
        if (_key is null) return;
        _mixer.Forget(_key);
        CloseRequested?.Invoke();
    }

    private void Changed()
    {
        _mixer.Save();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EffectCount));
        _mixer.SetFxOn(_key, _mixer.IsFxOn(_key)); // refreshes the effect count on the Volume list
    }

    public void Dispose() => _mixer.Updated -= OnMixerUpdated;
}
