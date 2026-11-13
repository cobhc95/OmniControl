using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Pages;
using OmniControl.App.Services;

namespace OmniControl.App.Fx;

/// <summary>
/// The FX window around one chain: its title, the row's effects on/off, the program's output device, the chain's total response and
/// live numbers (<see cref="Analysis"/>, fed from here), what the effects engine does with the chain (the same line and Fast-mode
/// caution as the Volume row) and the window options. The chain itself is <see cref="Chain"/>.
/// </summary>
public sealed partial class FxWindowViewModel : ObservableObject, IDisposable
{
    private readonly MixerService _mixer;
    private readonly EffectsEngine _engine;
    private readonly SoundModeService _modes;
    private readonly string? _key;

    /// <param name="key">The program key, an input key, or null for the master.</param>
    public FxWindowViewModel(MixerService mixer, EffectsEngine engine, SoundModeService modes, string? key, FxChainViewModel chain)
    {
        _mixer = mixer;
        _engine = engine;
        _modes = modes;
        _key = key;
        Chain = chain;
        var entry = key is null ? null : mixer.Find(key);
        Title = "FX: " + (key is null ? "Master" : MixerService.IsInput(key) ? mixer.InputName(key) : mixer.CombinedOf(key)?.Name ?? entry?.Name ?? "Program");
        IsProgram = entry is not null;
        if (entry is not null) Output = BuildOutput(entry);
        Analysis = new FxAnalysisViewModel(() => engine.LoadOf(key));
        ShowChain();
        chain.Edited += ShowChain;
        mixer.Updated += OnMixerUpdated;
        engine.StatusChanged += OnEngineStatus;
        modes.Changed += OnModesChanged;
    }

    public FxChainViewModel Chain { get; }

    /// <summary>The header's total response graph and its live numbers.</summary>
    public FxAnalysisViewModel Analysis { get; }
    public string Title { get; }
    /// <summary>The chain is a program's (not the master's, a recording device's or a combined device's).</summary>
    public bool IsProgram { get; }

    /// <summary>The program's output device picker, one device (null for the master and inputs; the Volume row offers several).</summary>
    public ChoiceItem? Output { get; }

    /// <summary>The mode, how it runs this chain and what the effects engine really does with it (the line the Volume row shows as its status's tooltip).</summary>
    public string EngineText => _engine.StatusOf(_key).LineFor(_mixer.SoundMode, _key);

    /// <summary>A program in Fast mode whose sound can't be recorded on this PC: the reason, as on its Volume row (null: none).</summary>
    public string? CaptureCaution => IsProgram ? _modes.ProgramCaptureCaution : null;
    public bool HasCaptureCaution => CaptureCaution is not null;

    /// <summary>The row's effects are switched on (the header's switch and Options > Chain on; the Volume row says "N effects, off" when not).</summary>
    public bool FxOn
    {
        get => _mixer.IsFxOn(_key);
        set { if (_mixer.IsFxOn(_key) != value) _mixer.SetFxOn(_key, value); }
    }

    /// <summary>A remembered program that isn't running can be forgotten from here.</summary>
    public bool CanForget => IsProgram && !_mixer.Running.Any(r => r.Key == _key && r.IsOpen);

    /// <summary>Option: FX windows stay above other programs too. Applies to every FX window as it opens.</summary>
    public bool KeepOnTop
    {
        get => AppSettings.Current.FxKeepOnTop;
        set { AppSettings.Current.FxKeepOnTop = value; AppSettings.Current.Save(); OnPropertyChanged(); }
    }

    public event Action? CloseRequested;

    [RelayCommand] private void CloseWindow() => CloseRequested?.Invoke();

    [RelayCommand]
    private void Forget()
    {
        if (_key is null) return;
        _mixer.Forget(_key);
        CloseRequested?.Invoke();
    }

    private void OnEngineStatus(string? key)
    {
        if (key != _key) return;
        OnPropertyChanged(nameof(EngineText));
        Analysis.RefreshLoad();
    }

    /// <summary>The chain as the engine that runs this row in the current sound mode plays it, with the row's on/off.</summary>
    private void ShowChain() => Analysis.Show(Chain.Effects, _mixer.SoundMode.MethodFor(_key).Engine, FxOn);

    private void OnMixerUpdated()
    {
        OnPropertyChanged(nameof(FxOn));
        OnPropertyChanged(nameof(CanForget));
        OnModesChanged(); // the sound mode is stored by the mixer
    }

    private void OnModesChanged()
    {
        ShowChain();
        OnPropertyChanged(nameof(EngineText));
        OnPropertyChanged(nameof(CaptureCaution));
        OnPropertyChanged(nameof(HasCaptureCaution));
    }

    private ChoiceItem BuildOutput(AppAudioEntry entry)
    {
        const string def = "Master (the default device)";
        var options = new List<string> { def };
        options.AddRange(_mixer.Devices.Select(d => d.Name));
        var current = entry.PlaysOn.Count switch
        {
            0 => def,
            1 => _mixer.DeviceNameOf(entry.PlaysOn[0]),
            _ => "Several devices (see the Volume page)",
        };
        if (!options.Contains(current)) options.Add(current);
        return new ChoiceItem("Output", options, current, name =>
        {
            if (string.IsNullOrEmpty(name)) return;
            if (name == def) _mixer.SetPlaysOn(_key!, Array.Empty<string>());
            else if (_mixer.Devices.FirstOrDefault(d => d.Name == name) is { } d) _mixer.SetPlaysOn(_key!, new[] { d.Id });
            // choosing the current entry that is not a plugged-in device keeps what is stored
        });
    }

    public void Dispose()
    {
        _mixer.Updated -= OnMixerUpdated;
        _engine.StatusChanged -= OnEngineStatus;
        _modes.Changed -= OnModesChanged;
        Chain.Edited -= ShowChain;
        Analysis.Dispose();
        Chain.Dispose();
    }
}
