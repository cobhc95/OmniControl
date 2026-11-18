using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Pages;
using OmniControl.App.Services;

namespace OmniControl.App.Fx;

/// <summary>
/// The FX window around one chain: its title, the row's effects on/off, the program's output device (Options > Plays on), the
/// chain's total response and live numbers (<see cref="Analysis"/>, fed from here), what the effects engine does with the chain (the
/// same line as the Volume row, in the status strip), the window options and its keyboard shortcuts. The chain
/// itself is <see cref="Chain"/>.
/// </summary>
public sealed partial class FxWindowViewModel : ObservableObject, IDisposable
{
    private readonly MixerService _mixer;
    private readonly EffectsEngine _engine;
    private readonly EffectsSupportService _support;
    private readonly DisplayFeeds _feeds;
    private readonly string? _key;

    /// <param name="key">The program key, an input key, or null for the master.</param>
    public FxWindowViewModel(MixerService mixer, EffectsEngine engine, EffectsSupportService support, DisplayFeeds feeds, string? key, FxChainViewModel chain)
    {
        _feeds = feeds;
        _mixer = mixer;
        _engine = engine;
        _support = support;
        _key = key;
        Chain = chain;
        var entry = key is null ? null : mixer.Find(key);
        Title = "FX: " + (key is null ? "Master" : MixerService.IsInput(key) ? mixer.InputName(key) : mixer.CombinedOf(key)?.Name ?? entry?.Name ?? "Program");
        IsProgram = entry is not null;
        RefreshOutput();
        Analysis = new FxAnalysisViewModel(() => engine.LoadOf(key));
        ShowChain();
        chain.Edited += ShowChain;
        mixer.Updated += OnMixerUpdated;
        engine.StatusChanged += OnEngineStatus;
        engine.ApoPlacementChanged += OnApoPlacementChanged;
        mixer.KeepFxInPlaceChanged += OnKeepFxInPlaceChanged;
        support.Changed += OnSupportChanged;
        feeds.Changed += OnDisplayFeedChanged;
        chain.PropertyChanged += OnChainChanged;
    }

    public FxChainViewModel Chain { get; }

    /// <summary>The toolbar's total response graph and its live numbers.</summary>
    public FxAnalysisViewModel Analysis { get; }
    public string Title { get; }
    /// <summary>The chain is a program's (not the master's, a recording device's or a combined device's).</summary>
    public bool IsProgram { get; }

    /// <summary>The program's output device as Options > Plays on offers it, one device (empty for the master and inputs; the Volume page offers several).</summary>
    [ObservableProperty] private IReadOnlyList<OutputChoice> _outputChoices = Array.Empty<OutputChoice>();

    /// <summary>The window's keyboard shortcuts, for Options > Keyboard shortcuts (the keys themselves are handled by the window and the chain panel).</summary>
    public static string KeyHelp { get; } = string.Join(Environment.NewLine, new[]
    {
        "Effects list",
        "Up/Down choose an effect; Shift+arrows, Ctrl+click, Ctrl+A or a box drawn on the empty part of the list select several.",
        "Enter gives the editor the keyboard; Tab or F6 switch between the list and the editor; Shift+Tab from the list goes to the response graph.",
        "Space switches the selected effects on or off; Delete removes them; Alt+Up / Alt+Down (or drag) move them; Ctrl+C / Ctrl+V copy and paste.",
        "Ctrl+S saves the chain to a file, Ctrl+O loads one. Ctrl+N adds an effect. A double-click on a row puts its editor in its own window, and back.",
        "Ctrl+- / Ctrl+= make the editor smaller or larger, Ctrl+0 puts it back to 100 %.",
        "Esc first leaves one effect selected, then closes the window.",
        "",
        "Response graph",
        "Enter pins the larger graph open or closes it; Esc closes it.",
        "",
        "Editor cards",
        PageViewModel.CardKeys,
        "",
        "Parametric equaliser",
        EqPanelItem.KeyHelp,
        "",
        "Graphic equalisers",
        GraphicEqItem.KeyHelp,
    });

    /// <summary>How the effects engine runs this chain and what it really does with it (the line the Volume row shows as its Method chip's tooltip).</summary>
    public string EngineText => _engine.StatusOf(_key).LineFor(_engine.MethodFor(_key), FxOn);

    /// <summary>The row's effects are switched on (the toolbar's switch and Options > Chain on; the Volume row says "N effects, off" when not).</summary>
    public bool FxOn
    {
        get => _mixer.IsFxOn(_key);
        set { if (_mixer.IsFxOn(_key) != value) _mixer.SetFxByUser(_key, value); }
    }

    /// <summary>Options > Keep OmniControl's effects in place (one setting for every chain Equalizer APO runs).</summary>
    public bool KeepFxInPlace
    {
        get => _mixer.KeepFxInPlace;
        set => _mixer.SetKeepFxInPlace(value);
    }

    public string KeepFxInPlaceText => EffectsRules.KeepInPlace;
    public string KeepFxInPlaceTip => EffectsRules.KeepInPlaceTip;

    /// <summary>This chain is sent to Equalizer APO now (the master there, or a program on a cable with Equalizer APO).</summary>
    private bool InApo => _engine.MethodOf(_key) is RunMethod.Apo or RunMethod.ApoCable;

    /// <summary>Equalizer APO's config was written without OmniControl's line and is left so: the status strip offers "Put back".</summary>
    public bool ShowsPutBack => InApo && _engine.ApoDisplaced;

    /// <summary>Equalizer APO's config also runs effects of its own: one tag in the status strip says so.</summary>
    public TagItem? OwnApoEffects => InApo && _engine.ApoOwnEffects ? new TagItem(EffectsRules.ApoOwnEffects, TagTone.Neutral, EffectsRules.ApoOwnEffectsTip) : null;

    /// <summary>The selected effect's meters or spectrum are fed with what you hear (Equalizer APO runs it where OmniControl can't see its sound): one tag says so, the detail is its tooltip.</summary>
    public TagItem? DisplayFed => Chain.Selected is { } s && _feeds.IsFed(s.Slot)
        ? _feeds.IsPaused(s.Slot) ? new TagItem(EffectsRules.DisplayNotFed, TagTone.Caution, EffectsRules.DisplayNotFedTip) : new TagItem(EffectsRules.DisplayFed, TagTone.Neutral, EffectsRules.DisplayFedTip)
        : null;

    private void OnDisplayFeedChanged() => OnPropertyChanged(nameof(DisplayFed));

    private void OnChainChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FxChainViewModel.Selected)) OnPropertyChanged(nameof(DisplayFed));
    }

    [RelayCommand] private void PutBack() => _engine.PutBackApo();

    /// <summary>A remembered program that isn't running can be forgotten from here.</summary>
    public bool CanForget => IsProgram && !_mixer.Running.Any(r => r.Key == _key && r.IsOpen);

    /// <summary>Option: FX windows stay above other programs too. Applies to every FX window as it opens.</summary>
    public bool KeepOnTop
    {
        get => AppSettings.Current.FxKeepOnTop;
        set { AppSettings.Current.FxKeepOnTop = value; AppSettings.Current.Save(); OnPropertyChanged(); }
    }

    /// <summary>The effect list's width as dragged at the splitter, kept for every FX window (0 = the standard width).</summary>
    public double ChainListWidth
    {
        get => AppSettings.Current.FxChainListWidth;
        set { AppSettings.Current.FxChainListWidth = value; AppSettings.Current.Save(); OnPropertyChanged(); }
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
        OnApoPlacementChanged();
        Analysis.RefreshLoad();
    }

    private void OnApoPlacementChanged()
    {
        OnPropertyChanged(nameof(ShowsPutBack));
        OnPropertyChanged(nameof(OwnApoEffects));
    }

    private void OnKeepFxInPlaceChanged() => OnPropertyChanged(nameof(KeepFxInPlace));

    /// <summary>The chain as the engine that runs this row plays it, with the row's on/off.</summary>
    private void ShowChain() => Analysis.Show(Chain.Effects, _engine.MethodFor(_key).Engine, _engine.ApoBlends, FxOn);

    private void OnMixerUpdated()
    {
        OnPropertyChanged(nameof(FxOn));
        OnPropertyChanged(nameof(CanForget));
        RefreshOutput();
        OnSupportChanged();
    }

    private void OnSupportChanged()
    {
        ShowChain();
        OnPropertyChanged(nameof(EngineText));
    }

    /// <summary>The Plays on entries: the default device, every device, and a "several devices" entry when the Volume page set more than one; rebuilt only when they change, so an open menu stays.</summary>
    private void RefreshOutput()
    {
        if (_key is null || _mixer.Find(_key) is not { } entry) return;
        var current = entry.PlaysOn.Count switch
        {
            0 => DefaultOutput,
            1 => _mixer.DeviceNameOf(entry.PlaysOn[0]),
            _ => SeveralOutputs,
        };
        var names = new List<string> { DefaultOutput };
        names.AddRange(_mixer.Devices.Select(d => d.Name));
        if (!names.Contains(current)) names.Add(current);
        if (OutputChoices.Select(c => (c.Name, c.IsChosen)).SequenceEqual(names.Select(n => (n, n == current)))) return;
        OutputChoices = names.Select(n => new OutputChoice(n, n == current, new RelayCommand(() => ChooseOutput(n)))).ToList();
    }

    private const string DefaultOutput = "Master (the default device)";
    private const string SeveralOutputs = "Several devices (see the Volume page)";

    private void ChooseOutput(string name)
    {
        if (name == DefaultOutput) _mixer.SetPlaysOn(_key!, Array.Empty<string>());
        else if (_mixer.Devices.FirstOrDefault(d => d.Name == name) is { } d) _mixer.SetPlaysOn(_key!, new[] { d.Id });
        // choosing the current entry that is not a plugged-in device keeps what is stored
    }

    public void Dispose()
    {
        _mixer.Updated -= OnMixerUpdated;
        _feeds.Changed -= OnDisplayFeedChanged;
        Chain.PropertyChanged -= OnChainChanged;
        _engine.StatusChanged -= OnEngineStatus;
        _engine.ApoPlacementChanged -= OnApoPlacementChanged;
        _mixer.KeepFxInPlaceChanged -= OnKeepFxInPlaceChanged;
        _support.Changed -= OnSupportChanged;
        Chain.Edited -= ShowChain;
        Analysis.Dispose();
        Chain.Dispose();
    }
}

/// <summary>One entry of the FX window's Options > Plays on: a device, whether the program plays on it now, and choosing it.</summary>
public sealed record OutputChoice(string Name, bool IsChosen, IRelayCommand Choose);
