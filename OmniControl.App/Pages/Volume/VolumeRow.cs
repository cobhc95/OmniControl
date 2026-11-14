using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// One row of the Volume Mixer, whatever it stands for (the master, a program, a microphone, a combined device): name and icon, the
/// sentence under the name ("→ Speakers · 2 effects", whose underlined words are controls: the devices open the Wiring tab at this
/// row, the effects open the row's FX window), level and mute, a meter. Changes go out through the actions the page gives; the page
/// fills the rest in. Where the row plays is only shown here: the Wiring tab changes it.
/// </summary>
public abstract partial class VolumeRow : ObservableObject, IEditorItem
{
    private bool _syncing;

    /// <param name="newGate">Makes the row's mode gates (each with the page's way to switch modes).</param>
    protected VolumeRow(string key, Func<ModeGate> newGate)
    {
        Key = key;
        EffectsGate = newGate();
        CaptureGate = newGate();
        // The gate lives as long as the row, so this subscription needs no undoing.
        CaptureGate.PropertyChanged += (_, _) => OnCaptureChanged();
        EffectsGate.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowsEffectsSwitch));
            OnPropertyChanged(nameof(FxTip));
        };
    }

    string IEditorItem.Label => Name;

    /// <summary>The row's key (a program key, <see cref="MixerService.MasterKey"/>, an input key or a combined device's key).</summary>
    public string Key { get; }

    /// <summary>The key its FX chain is stored under (null for the master).</summary>
    public virtual string? ChainKey => Key;

    public MeterModel Meter { get; } = new();

    // ---- what the page supplies ----

    public Action<int>? VolumeChanged { get; init; }
    public Action<bool>? MuteChanged { get; init; }
    public Action<bool>? FxToggled { get; init; }
    /// <summary>Opens this row's FX window (the effects link and the FX button).</summary>
    public Action? OpenWindowRequested { get; init; }

    /// <summary>The device words were clicked: the page opens Wiring at this row.</summary>
    public Action? WiringRequested { get; init; }

    /// <summary>Removes the row (a remembered program, a combined device); null = it can't be removed.</summary>
    public Action? Forget { get; init; }

    /// <summary>Runs the recording Test on this program (null = not a program).</summary>
    public Func<Task>? Test { get; init; }

    // ---- identity ----

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Initial))] private string _name = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPicture), nameof(ShowsInitial))] private Bitmap? _picture;

    /// <summary>A stroked 24 x 24 icon when the row has no picture (a microphone, a combined device).</summary>
    public virtual string? Glyph => null;

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public bool HasPicture => Picture is not null;
    public bool HasGlyph => Glyph is not null;
    public bool ShowsInitial => Picture is null && Glyph is null;

    /// <summary>A small tag after the name in the Wiring Source column ("Combined"); null = none.</summary>
    public virtual string? SourceTag => null;
    public bool HasSourceTag => SourceTag is not null;

    /// <summary>Remembered programs that are not open are drawn quieter.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RowOpacity))] private bool _isQuiet;
    public double RowOpacity => IsQuiet ? QuietOpacity : 1;

    /// <summary>How much a quiet row (a closed program, a virtual cable that is not used) is dimmed.</summary>
    public const double QuietOpacity = 0.7;

    // ---- level ----

    public virtual bool HasVolume => true;
    /// <summary>Left/right balance (-100..100), on the rows that have one (the master and programs).</summary>
    public virtual bool HasPan => false;
    public Action<int>? PanChanged { get; init; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PanText))] private int _pan;
    public string PanText => Pan == 0 ? "C" : Pan < 0 ? $"L{-Pan}" : $"R{Pan}";
    public virtual string VolumeTip => "Volume";
    [ObservableProperty] private int _volume;
    [ObservableProperty] private bool _muted;

    // ---- the sentence ----

    /// <summary>Plain words at the start of the sentence ("Default microphone"); empty = none.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasLead))] private string _lead = string.Empty;
    public bool HasLead => Lead.Length > 0;

    /// <summary>The words before a row's devices: where it plays, where it records from.</summary>
    protected const string PlaysOnArrow = "→", RecordsFromArrow = "←";

    /// <summary>"→" where the row plays, "←" where it records from; empty when it has no wiring of its own (a microphone).</summary>
    public virtual string RouteArrow => string.Empty;
    public bool HasRoute => RouteArrow.Length > 0;

    /// <summary>The dot between the devices and the effects (combined speakers have no effects to follow it).</summary>
    public bool ShowsRouteSeparator => HasRoute && HasEffects;

    /// <summary>The devices it plays on or records from, as the Wiring tab sets them ("Speakers + Interface 2").</summary>
    [ObservableProperty] private string _routeText = string.Empty;

    public virtual bool HasEffects => true;
    public ModeGate EffectsGate { get; }

    /// <summary>The FX button's tooltip: why the effects can't be edited now (it is dimmed), else what it does.</summary>
    public string FxTip => EffectsGate.Reason ?? "Opens this row's FX window";

    /// <summary>True: the effects' Switch link is offered only when the row has a chain to run (a microphone with no effects needs nothing).</summary>
    protected virtual bool SwitchNeedsEffects => false;

    /// <summary>The Switch link next to the effects text.</summary>
    public bool ShowsEffectsSwitch => EffectsGate.CanSwitch && (!SwitchNeedsEffects || FxCount > 0);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EffectsText))] private bool _fxOn;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EffectsText), nameof(ShowsCaptureCaution), nameof(ShowsCaptureSwitch), nameof(ShowsStatus), nameof(ShowsTest), nameof(ShowsEffectsSwitch))] private int _fxCount;

    public string EffectsText => (FxCount == 0 ? "no effects" : FxCount == 1 ? "1 effect" : $"{FxCount} effects") + (FxOn || FxCount == 0 ? string.Empty : ", off");

    /// <summary>What the engine says about this row (empty when nothing runs or waits), after the sentence.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsStatus))] private string _status = string.Empty;

    /// <summary>The one step that fixes what the status says is missing (a microphone in Fast mode: Choose devices; a program set to Virtual cable with no cable: Use Recording); null = none.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFix))] private ModeCardAction? _fix;
    public bool HasFix => Fix is not null;

    /// <summary>The status is shown unless the caution tag already says why nothing runs.</summary>
    public bool ShowsStatus => Status.Length > 0 && !ShowsCaptureCaution;

    /// <summary>The status's tooltip, with the FX window's longer line: the mode, how it runs the effects and what really happens ("Fast mode · effects by recording · Effects running · about 40 ms delay"), then what to do next.</summary>
    [ObservableProperty] private string _modeLine = string.Empty;

    /// <summary>A program in Fast mode whose sound can't be recorded on this PC: the reason, as a caution tag, and a Switch link to Full control.</summary>
    public ModeGate CaptureGate { get; }

    /// <summary>The caution shows only on a row that has effects, where it matters (one tag per fact, not on every program).</summary>
    public bool ShowsCaptureCaution => !CaptureGate.IsAvailable && FxCount > 0;

    /// <summary>The caution's Switch link: only when the mode it names can be chosen on this PC.</summary>
    public bool ShowsCaptureSwitch => ShowsCaptureCaution && CaptureGate.CanSwitch;

    /// <summary>The "Method" choice of a program or the master (how its effects run in Full control); null on every other row.</summary>
    public RouteChoice? RouteChoice { get; init; }

    /// <summary>The Method button shows in Full control, the only place the choice applies: on the master, and on a program with effects.</summary>
    [ObservableProperty] private bool _showsRouteChoice;

    [ObservableProperty] private bool _canForget;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsTest))] private bool _canTest;

    /// <summary>The recording Test, on an open program that has effects; dimmed with the reason while it is not playing (<see cref="TestWhy"/>).</summary>
    public bool ShowsTest => CanTest && FxCount > 0;

    /// <summary>Why the Test can't run now (the program must be playing); null = it can.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanRunTest), nameof(TestButtonTip))] private string? _testWhy;

    public bool CanRunTest => TestWhy is null;
    public string TestButtonTip => TestWhy ?? TestDefaultTip;
    private const string TestDefaultTip = "Plays a few seconds through the effects, checks the program can be recorded while its own sound is silenced, measures the delay";

    /// <summary>The Test's result as one short line, and its full explanation for the tooltip.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTestNote))] private string _testNote = string.Empty;
    [ObservableProperty] private string _testTip = string.Empty;
    public bool HasTestNote => TestNote.Length > 0;

    partial void OnVolumeChanged(int value) { if (!_syncing) VolumeChanged?.Invoke(value); }
    partial void OnPanChanged(int value) { if (!_syncing) PanChanged?.Invoke(value); }
    partial void OnMutedChanged(bool value) { if (!_syncing) MuteChanged?.Invoke(value); }
    partial void OnFxOnChanged(bool value) { if (!_syncing) FxToggled?.Invoke(value); }

    private void OnCaptureChanged()
    {
        OnPropertyChanged(nameof(ShowsCaptureCaution));
        OnPropertyChanged(nameof(ShowsCaptureSwitch));
        OnPropertyChanged(nameof(ShowsStatus));
    }

    [RelayCommand] private void ToggleMute() => Muted = !Muted;

    [RelayCommand]
    private void OpenWindow()
    {
        if (EffectsGate.IsAvailable) OpenWindowRequested?.Invoke();
    }

    [RelayCommand] private void ShowWiring() => WiringRequested?.Invoke();
    [RelayCommand] private void Remove() => Forget?.Invoke();

    [RelayCommand]
    private async Task RunTest()
    {
        if (Test is not null) await Test();
    }

    /// <summary>Updates the level and effects from the system without sending them back.</summary>
    public void Sync(int volume, bool muted, bool fxOn, int fxCount)
    {
        _syncing = true;
        Volume = volume;
        Muted = muted;
        FxOn = fxOn;
        FxCount = fxCount;
        _syncing = false;
    }

    /// <summary>Updates the balance from the system without sending it back.</summary>
    protected void SyncPan(int pan)
    {
        _syncing = true;
        Pan = pan;
        _syncing = false;
    }
}
