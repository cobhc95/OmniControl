using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Fx;
using OmniControl.App.Pages.Volume.Easy.Flow;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>
/// The Sound drawer of one Easy row (the master "Everything", a program or a microphone): preset chips, a small response curve and five
/// friendly sliders. It edits the row's own Parametric equaliser in the same chain the Advanced FX window edits (<see cref="EasyEq"/>
/// finds or makes it, through <see cref="MixerService"/>), so there is no second copy of the sound. It listens to the mixer's
/// <see cref="MixerService.FxChanged"/> for its own chain, so the FX window's edits move the sliders, chips and curve at once, and
/// <see cref="Refresh"/> reads it again for everything else. When the effect no longer has the five-slider layout
/// (<see cref="EasyEq.IsCustom"/>) the sliders are greyed and nothing here writes bands without asking first. It does not know the row or the page.
/// </summary>
public sealed partial class EasySound : ObservableObject, IDisposable
{
    private readonly MixerService _mixer;
    private readonly EffectsEngine _engine;
    private readonly string? _key;
    private readonly Action _openFull;
    private readonly Func<string?> _whyNot;
    private readonly Func<EasyConfirm, Task<bool>> _confirm;
    private readonly List<EasyChip> _presetChips;
    private int[] _own = EasyEq.Flat();
    private bool _hasOwn;

    /// <param name="key">The row's chain key (null = the master).</param>
    /// <param name="openFull">Opens the row's own FX window ("More controls").</param>
    /// <param name="whyNot">Why the row's sound cannot be applied now (null = it can): the drawer greys itself with this sentence.</param>
    /// <param name="confirm">Asks before a shape made in the full effects window is replaced.</param>
    public EasySound(MixerService mixer, EffectsEngine engine, string? key, Action openFull, Func<string?> whyNot, Func<EasyConfirm, Task<bool>> confirm)
    {
        _mixer = mixer;
        _engine = engine;
        _key = key;
        _openFull = openFull;
        _whyNot = whyNot;
        _confirm = confirm;
        Bands = EasyEq.Parts.Select((p, i) => new EasyBandItem(i, p.Name, OnBandMoved)).ToList();
        _presetChips = EasyEq.Presets.Select(p => new EasyChip(p.Name, () => _ = ApplyAsync(p.Gains))).ToList();
        OwnChip = new EasyChip("My own", () => _ = ApplyAsync(_own)) { CanPick = false };
        Chips = _presetChips.Append(OwnChip).ToList();
        Refresh();
        mixer.FxChanged += OnFxChanged;
        engine.PlanChanged += Refresh;
    }

    public IReadOnlyList<EasyBandItem> Bands { get; }

    /// <summary>The preset chips, then "My own".</summary>
    public IReadOnlyList<EasyChip> Chips { get; }

    private EasyChip OwnChip { get; }

    /// <summary>Whether the sound can be applied now; when not, the drawer is greyed and says why.</summary>
    public Availability Availability { get; } = new();

    /// <summary>Whether the five sliders can describe the effect; when not (it was shaped in the full effects window) they are greyed.</summary>
    public Availability SlidersAvailability { get; } = new();

    /// <summary>The effect has a shape the five sliders cannot describe: the line "Shaped in the full effects window" and its way back show.</summary>
    [ObservableProperty] private bool _isCustom;

    /// <summary>Why the sliders are greyed (the line's tooltip).</summary>
    public string CustomWhy => "The effect has bands the five sliders cannot show: one was moved, added, removed or changed type, or it has a gain beyond "
        + $"{EasyEq.MaxGainDb} dB or an overall gain. Change it in the full effects window, or go back to the simple sliders.";

    [ObservableProperty] private bool _isOpen;

    /// <summary>The small response curve, alive only while the drawer is open (its analysis runs a timer).</summary>
    [ObservableProperty] private FxAnalysisViewModel? _curve;

    /// <summary>One calm line about the sound when something needs saying (made in Advanced, effects off); empty = nothing.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    /// <summary>The name of the sound now: a preset's, or "My own".</summary>
    [ObservableProperty] private string _name = EasyEq.Presets[0].Name;

    /// <summary>True when the row has a sound set (not flat, and its effects on): what the Sound button and the footer say.</summary>
    [ObservableProperty] private bool _isActive;

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            Curve = new FxAnalysisViewModel(() => _engine.LoadOf(_key));
            ShowCurve();
        }
        else CloseCurve();
    }

    private void CloseCurve()
    {
        Curve?.Dispose();
        Curve = null;
    }

    [RelayCommand] private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand] private void OpenFull() => _openFull();

    /// <summary>Sets the next preset as a chip would (<see cref="ApplyAsync"/>); for the hotkey that steps through presets. True when it was applied.</summary>
    public Task<bool> NextPresetAsync()
    {
        var at = EasyEq.Presets.ToList().FindIndex(p => p.Name == Name);
        return ApplyAsync(EasyEq.Presets[(at + 1) % EasyEq.Presets.Count].Gains);
    }

    /// <summary>"Back to simple sliders": after a yes, the effect becomes the Easy layout at its closest match, so the sound changes as little as possible.</summary>
    [RelayCommand]
    private async Task BackToSimple()
    {
        if (EasyEq.Find(_mixer.FxChain(_key)) is not { } slot || !EasyEq.IsCustom(slot)) return;
        var fit = EasyEq.BestFit(slot);
        var question = new EasyConfirm("Use simple sliders?",
            "Your shape is replaced by the five sliders at the closest match, so the sound changes as little as possible. Cancel keeps your shape.", "Use simple sliders");
        if (await _confirm(question)) Commit(fit);
    }

    private void OnBandMoved(EasyBandItem moved)
    {
        var gains = Bands.Select(b => b.Value).ToArray();
        gains[moved.Index] = moved.Value;
        // A slider that only settled into its range while it was laid out is not a change of the sound. The sliders are greyed while the
        // effect is custom, so a move here never replaces a shape.
        if (!gains.SequenceEqual(EasyEq.Read(_mixer.FxChain(_key)))) Commit(gains);
    }

    /// <summary>A chip was picked: writes its gains, but first asks when that would replace a shape made in the full effects window. True when written.</summary>
    private async Task<bool> ApplyAsync(IReadOnlyList<int> gains)
    {
        if (EasyEq.HasCustom(_mixer.FxChain(_key)))
        {
            var question = new EasyConfirm("Replace your shape?",
                "This replaces the shape you made in the full effects window with five simple sliders. Cancel keeps your shape.", "Use simple sliders");
            if (!await _confirm(question)) return false;
        }
        Commit(gains);
        return true;
    }

    /// <summary>Writes the gains into the row's chain and tells the engine; a flat sound with nothing else in the chain switches the effects off, any other sound switches them on.</summary>
    private void Commit(IReadOnlyList<int> gains)
    {
        var chain = _mixer.FxChain(_key);
        EasyEq.Write(chain, gains);
        var on = EasyEq.EffectsOnFor(chain, gains, _mixer.IsFxOn(_key));
        if (on != _mixer.IsFxOn(_key)) _mixer.SetFxOn(_key, on);
        else _mixer.SaveFx(_key);
    }

    private void OnFxChanged(string? key)
    {
        if (key == _key) Refresh();
    }

    /// <summary>Reads the chain again: the sliders, the chips, the curve and the notes follow whatever the stored sound is now.</summary>
    public void Refresh()
    {
        var chain = _mixer.FxChain(_key);
        var gains = EasyEq.Read(chain);
        foreach (var band in Bands) band.Sync(gains[band.Index]);
        IsCustom = EasyEq.HasCustom(chain);
        if (IsCustom) SlidersAvailability.MakeUnavailable("Shaped in the full effects window");
        else SlidersAvailability.MakeAvailable();
        var preset = IsCustom ? null : EasyEq.PresetOf(gains);
        if (preset is null && !IsCustom)
        {
            _own = gains;
            _hasOwn = true;
        }
        foreach (var chip in _presetChips) chip.IsSelected = preset is not null && chip.Name == preset.Name;
        OwnChip.IsSelected = preset is null;
        OwnChip.CanPick = _hasOwn && preset is not null;
        Name = preset?.Name ?? OwnChip.Name;
        IsActive = !EasyEq.IsFlat(gains) && _mixer.IsFxOn(_key);
        Note = !EasyEq.IsFlat(gains) && !_mixer.IsFxOn(_key) ? "Effects are off; moving a slider turns them on."
            : string.Empty;
        if (_whyNot() is { } why) Availability.MakeUnavailable(why);
        else Availability.MakeAvailable();
        ShowCurve();
    }

    private void ShowCurve() => Curve?.Show(_mixer.FxChain(_key), _engine.MethodFor(_key).Engine, _engine.ApoBlends, _mixer.IsFxOn(_key));

    public void Dispose()
    {
        _mixer.FxChanged -= OnFxChanged;
        _engine.PlanChanged -= Refresh;
        CloseCurve();
    }
}
