using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// One row of the Volume Mixer, whatever it stands for (the master, a program, a microphone, a combined device), in the columns of
/// <c>docs/mockups/mixer-rows-knobs.html</c>: icon, name and its state word, where it plays (<see cref="PlaysOn"/>: the Wiring tab's own
/// device menu), how its effects run (the <see cref="Method"/> chip, which opens the row's Method menu, and the short fact under it), pan
/// and volume knobs, mute, a meter and the FX button (lit while effects run; it opens and closes the row's FX window). Changes go out
/// through the actions the page gives; the page fills the rest in.
/// </summary>
public abstract partial class VolumeRow : ObservableObject, IEditorItem
{
    private bool _syncing;

    /// <remarks>The page's actions (<see cref="VolumeChanged"/>...) are set by its object initialiser after this constructor.</remarks>
    protected VolumeRow(string key) => Key = key;

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

    /// <summary>The FX button: opens this row's FX window, or closes it when it is open.</summary>
    public Action? FxWindowToggled { get; init; }

    /// <summary>Opens the Wiring tab at this row (a combined device's members are changed there); null = no jump.</summary>
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

    /// <summary>The small muted word under the name: "playing", "not open", "default"; empty = none.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStateWord))] private string _stateWord = string.Empty;
    public bool HasStateWord => StateWord.Length > 0;

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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PanText), nameof(PanTip))] private int _pan;

    /// <summary>The pan knob's words: "C" in the middle, else the side and how far ("L 20", "R 20").</summary>
    public string PanText => Pan == 0 ? "C" : Pan < 0 ? $"L {-Pan}" : $"R {Pan}";

    /// <summary>The knobs' tooltips: what they set and the exact value.</summary>
    public string PanTip => $"Pan (left to right): {PanText}";
    public string VolumeValueTip => $"{VolumeTip}: {Volume}";

    /// <summary>The volume knob's default (a double-click or Home goes back to it), and the pan knob's (the centre).</summary>
    public const double VolumeReset = 100, PanReset = 0;

    public virtual string VolumeTip => "Volume";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(VolumeValueTip))] private int _volume;
    [ObservableProperty] private bool _muted;

    // ---- where it plays ----

    /// <summary>
    /// The "Plays on" chip's menu (the master and programs): the same menu and rules as the Wiring tab's cell (<see cref="PlaysOnMenu"/>),
    /// here a pick makes the device the only one. Null on rows whose devices are not picked from a list.
    /// </summary>
    public PlaysOnMenu? PlaysOn { get; init; }

    public bool HasPlaysOnMenu => PlaysOn is not null;

    /// <summary>A combined device's chip opens the Wiring tab at its row, where its members are added and taken away.</summary>
    public bool PlaysOnOpensWiring => PlaysOn is null && WiringRequested is not null;

    /// <summary>A microphone's chip only names the device it records from.</summary>
    public bool PlaysOnIsFixed => PlaysOn is null && WiringRequested is null;

    /// <summary>The devices it plays on or records from, as the routing model says ("Speakers + Interface 2", "Master").</summary>
    [ObservableProperty] private string _routeText = string.Empty;

    // ---- how its effects run ----

    /// <summary>The Method chip: the method in use now (its words and colour).</summary>
    [ObservableProperty] private MethodChip _method = MethodChip.Of(RunMethod.None);

    /// <summary>The "Method" menu of a program or the master; null on rows that have no choice (a microphone, a combined device).</summary>
    public RouteChoice? RouteChoice { get; init; }

    public bool HasMethodMenu => RouteChoice is not null;

    /// <summary>The short fact the engine gives (from <see cref="Audio.Engine.EngineStatus.Fact"/>: "70 ms", "no delay", "Starts when it plays").</summary>
    [ObservableProperty] private string _fact = string.Empty;

    public virtual bool HasEffects => true;
    /// <summary>The FX button's tooltip: what it does.</summary>
    public string FxTip => FxRuns ? FxRunningTip : FxOpenTip;
    private const string FxRunningTip = "Effects on. Opens or closes this row's FX window", FxOpenTip = "Opens or closes this row's FX window";

    [ObservableProperty] private bool _fxOn;
    [ObservableProperty] private int _fxCount;

    /// <summary>The FX button glows: the effects are on and at least one of them is switched on (<see cref="FxSlot.AnyRuns"/>).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FxTip))] private bool _fxRuns;

    /// <summary>What the engine says about this row in one short line (empty when nothing runs or waits); the Easy page shows it.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsStatus))] private string _status = string.Empty;

    /// <summary>The one step that fixes what the status says is missing (a program set to Virtual cable with no cable: Use Recording); null = none.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFix))] private CardAction? _fix;
    public bool HasFix => Fix is not null;

    public bool ShowsStatus => Status.Length > 0;

    /// <summary>The long line behind the Method chip and the fact: the setup, how it runs the effects and what really happens, then what to do next.</summary>
    [ObservableProperty] private string _engineLine = string.Empty;

    [ObservableProperty] private bool _canForget;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsTest))] private bool _canTest;

    /// <summary>The recording Test (in the Method menu), on an open program that has effects; dimmed with the reason while it is not playing (<see cref="TestWhy"/>).</summary>
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

    // ---- the line under the Method chip ----

    /// <summary>The words under the Method chip: the Test's result while there is one, else the fact.</summary>
    [ObservableProperty] private string _factLine = string.Empty;
    [ObservableProperty] private string _factTip = string.Empty;

    /// <summary>The one link after those words: the step that fixes what is missing; null = none.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFactLink))] private CardAction? _factLink;
    public bool HasFactLink => FactLink is not null;

    /// <summary>One fact per line (AGENTS.md principle 11): what the line and its one link say now.</summary>
    private void RefreshFact()
    {
        FactLine = HasTestNote ? TestNote : Fact;
        FactTip = HasTestNote ? TestTip : EngineLine;
        FactLink = Fix;
    }

    partial void OnFactChanged(string value) => RefreshFact();
    partial void OnEngineLineChanged(string value) => RefreshFact();
    partial void OnFixChanged(CardAction? value) => RefreshFact();
    partial void OnTestNoteChanged(string value) => RefreshFact();
    partial void OnTestTipChanged(string value) => RefreshFact();

    partial void OnFxCountChanged(int value)
    {
        OnPropertyChanged(nameof(ShowsTest));
        RefreshFact();
    }

    partial void OnVolumeChanged(int value) { if (!_syncing) VolumeChanged?.Invoke(value); }
    partial void OnPanChanged(int value) { if (!_syncing) PanChanged?.Invoke(value); }
    partial void OnMutedChanged(bool value) { if (!_syncing) MuteChanged?.Invoke(value); }
    partial void OnFxOnChanged(bool value) { if (!_syncing) FxToggled?.Invoke(value); }

    [RelayCommand] private void ToggleMute() => Muted = !Muted;

    [RelayCommand]
    private void ToggleWindow()
    {
        FxWindowToggled?.Invoke();
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
