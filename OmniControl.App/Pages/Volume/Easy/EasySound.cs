using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Fx;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>
/// The Sound drawer of one Easy row (the master "Everything", a program or a microphone): preset chips, a small response curve and five
/// friendly sliders. It edits the row's own Parametric equaliser in the same chain the Advanced FX window edits (<see cref="EasyEq"/>
/// finds or makes it, through <see cref="MixerService"/>), so there is no second copy of the sound; <see cref="Refresh"/> reads it again
/// whenever something may have changed it elsewhere. It does not know the row or the page.
/// </summary>
public sealed partial class EasySound : ObservableObject, IDisposable
{
    private readonly MixerService _mixer;
    private readonly EffectsEngine _engine;
    private readonly string? _key;
    private readonly Action _openFull;
    private readonly Func<string?> _whyNot;
    private readonly List<EasyChip> _presetChips;
    private int[] _own = EasyEq.Flat();
    private bool _hasOwn;

    /// <param name="key">The row's chain key (null = the master).</param>
    /// <param name="openFull">Opens the row's own FX window ("More controls").</param>
    /// <param name="whyNot">Why the row's sound cannot be applied now (null = it can): the drawer greys itself with this sentence.</param>
    public EasySound(MixerService mixer, EffectsEngine engine, string? key, Action openFull, Func<string?> whyNot)
    {
        _mixer = mixer;
        _engine = engine;
        _key = key;
        _openFull = openFull;
        _whyNot = whyNot;
        Bands = EasyEq.Parts.Select((p, i) => new EasyBandItem(i, p.Name, OnBandMoved)).ToList();
        _presetChips = EasyEq.Presets.Select(p => new EasyChip(p.Name, () => Commit(p.Gains))).ToList();
        OwnChip = new EasyChip("My own", () => Commit(_own)) { CanPick = false };
        Chips = _presetChips.Append(OwnChip).ToList();
        Refresh();
    }

    public IReadOnlyList<EasyBandItem> Bands { get; }

    /// <summary>The preset chips, then "My own".</summary>
    public IReadOnlyList<EasyChip> Chips { get; }

    private EasyChip OwnChip { get; }

    /// <summary>Whether the sound can be applied now; when not, the drawer is greyed and says why.</summary>
    public Availability Availability { get; } = new();

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

    /// <summary>Sets the five gains as a preset or the user's own sound would (<see cref="Commit"/>); for the hotkey that steps through presets.</summary>
    public void NextPreset()
    {
        var at = EasyEq.Presets.ToList().FindIndex(p => p.Name == Name);
        Commit(EasyEq.Presets[(at + 1) % EasyEq.Presets.Count].Gains);
    }

    private void OnBandMoved(EasyBandItem moved)
    {
        var gains = Bands.Select(b => b.Value).ToArray();
        gains[moved.Index] = moved.Value;
        // A slider that only settled into its range while it was laid out is not a change of the sound.
        if (!gains.SequenceEqual(EasyEq.Read(_mixer.FxChain(_key)))) Commit(gains);
    }

    /// <summary>Writes the gains into the row's chain and tells the engine; a flat sound with nothing else in the chain switches the effects off, any other sound switches them on.</summary>
    private void Commit(IReadOnlyList<int> gains)
    {
        var chain = _mixer.FxChain(_key);
        EasyEq.Write(chain, gains);
        var on = EasyEq.EffectsOnFor(chain, gains, _mixer.IsFxOn(_key));
        if (on != _mixer.IsFxOn(_key)) _mixer.SetFxOn(_key, on);
        else _mixer.SaveFx(_key);
        Refresh();
    }

    /// <summary>Reads the chain again: the sliders, the chips, the curve and the notes follow whatever the stored sound is now.</summary>
    public void Refresh()
    {
        var chain = _mixer.FxChain(_key);
        var gains = EasyEq.Read(chain);
        foreach (var band in Bands) band.Sync(gains[band.Index]);
        var preset = EasyEq.PresetOf(gains);
        if (preset is null)
        {
            _own = gains;
            _hasOwn = true;
        }
        foreach (var chip in _presetChips) chip.IsSelected = preset is not null && chip.Name == preset.Name;
        OwnChip.IsSelected = preset is null;
        OwnChip.CanPick = _hasOwn && preset is not null;
        Name = preset?.Name ?? OwnChip.Name;
        IsActive = !EasyEq.IsFlat(gains) && _mixer.IsFxOn(_key);
        Note = EasyEq.HasOtherBands(chain) ? "Made in Advanced; moving a slider replaces it."
            : !EasyEq.IsFlat(gains) && !_mixer.IsFxOn(_key) ? "Effects are off; moving a slider turns them on."
            : string.Empty;
        if (_whyNot() is { } why) Availability.MakeUnavailable(why);
        else Availability.MakeAvailable();
        ShowCurve();
    }

    private void ShowCurve() => Curve?.Show(_mixer.FxChain(_key), _engine.MethodFor(_key).Engine, _mixer.IsFxOn(_key));

    public void Dispose() => CloseCurve();
}
