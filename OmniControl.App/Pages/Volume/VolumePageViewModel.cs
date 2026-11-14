using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Volume page, two tabs. Mixer: Outputs (the master, one row per program, open or remembered, and combined speakers) and
/// Microphones (every recording device and combined microphone); each row says where it plays (read only: its words jump to Wiring)
/// and what effects it has, which open in the row's FX window. Wiring: the sound mode, the cable status and "Combine devices", a table
/// of where each source plays or records from and the cable it uses, and the combined devices. Both tabs read the same routing model.
/// Sound state lives in <see cref="MixerService"/>; what can run in <see cref="SoundModeService"/>; what runs in
/// <see cref="EffectsEngine"/>. This class turns those into rows and keeps them up to date. Partial files: rows (.Rows), microphones
/// and combined devices (.Inputs), the routing model, sentences and gates (.Routing), the Wiring tab (.Wiring), the sound mode panel
/// (.SoundMode), combining (.Combine), hotkeys and meters (.Hotkeys).
/// </summary>
public sealed partial class VolumePageViewModel : PageViewModel, IDisposable
{
    private readonly MixerService _mixer;
    private readonly SoundModeService _modes;
    private readonly EffectsEngine _engine;
    private readonly EngineInstaller _installer;
    private readonly Func<EngineMessage, Task<bool>> _ask;
    private readonly Func<CombineDeviceViewModel, Task<bool>> _combine;
    private readonly OsdService _osd;
    private readonly Action<string> _report;
    private readonly Action<string?> _openFx;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _meterTimer = new() { Interval = Timing.MeterFrame };

    public override string Title => "Volume";

    public override string KeyHelp => string.Join(Environment.NewLine,
        "Ctrl+Tab / Ctrl+Shift+Tab switch between Mixer and Wiring.",
        "Mixer: Left/Right choose a part of a row (the underlined words, pan, volume, mute, FX); Enter opens or adjusts it.",
        "Wiring: Enter opens a row's menu; Esc closes it.");
    public override string Icon => "M3,9 H7 L12,4 V20 L7,15 H3 Z M15,8.5 A5,5 0 0 1 15,15.5 L16.2,17 A7,7 0 0 0 16.2,7 Z";
    public override string Keywords => "volume sound audio mixer wiring master apps programs pan balance mute fx effects chain equaliser eq compressor plays on output device microphone combine sound mode cable";

    /// <summary>The page's tabs (their names are also the search groups of what each shows).</summary>
    private const string MixerTab = "Mixer", WiringTab = "Wiring";

    public PageTabs Tabs { get; } = new(MixerTab, WiringTab);
    public bool ShowsMixer => Tabs.Current == MixerTab;
    public bool ShowsWiring => Tabs.Current == WiringTab;

    /// <summary>Outputs: the master, programs, combined speakers, then the "Add program" row. Open unless the user closed it.</summary>
    public VolumeGroup Outputs { get; } = new("Volume/Outputs", "Outputs")
    {
        ExpandedByDefault = true,
        Tip = "The → and ← words open Wiring; the effects words and FX open the row's FX window. Programs are remembered after they close; × forgets one.",
    };

    /// <summary>Microphones: recording devices and combined microphones. Open unless the user closed it (key kept from "Inputs").</summary>
    public VolumeGroup Inputs { get; } = new("Volume/Inputs", "Microphones") { ExpandedByDefault = true };

    /// <summary>The Wiring tab's "Sound mode" button and panel.</summary>
    public SoundModeChoice SoundMode { get; }

    /// <summary>"Combine devices" (the Wiring tab's button, its combined devices): dimmed in Fast mode, with the reason and a Switch link.</summary>
    public ModeGate CombineGate { get; }

    /// <summary>The Mixer's quiet mode words ("Fast mode"), which open Wiring.</summary>
    [ObservableProperty] private string _modeIndicator = string.Empty;

    /// <summary>The Wiring tab's cable chip: Full control's own first status line ("Ready · 1 virtual cable", "Needs VB-CABLE"); null before the first look.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCableStatus))] private ModeStatus? _cableStatus;
    public bool HasCableStatus => CableStatus is not null;

    /// <summary>Install VB-CABLE beside the chip while none is installed; null = nothing to install.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCableSetup))] private ModeCardAction? _cableSetup;
    public bool HasCableSetup => CableSetup is not null;

    /// <summary>Search cards for the page's own settings (the sound mode and combining), next to every row.</summary>
    private readonly GalleryItem _soundModeCard;
    private readonly ButtonItem _combineCard;

    public override IEnumerable<SearchEntry> SearchEntries =>
        new SearchEntry[]
        {
            Entry(_soundModeCard, "Sound mode", "fast full control mode apo equalizer virtual cable vb-cable delay microphone mic capture device selector", WiringTab),
            Entry(_combineCard, "Combine devices", "combine merge speakers microphones mic several devices together cable", WiringTab),
        }
        .Concat(AllRows().Select(r => Entry(r, Title, "sound audio mixer app program pan fx effects output device microphone", MixerTab)))
        .Concat(WiringRows().Select(w => Entry(w, (w.Source is InputItem ? WiringInputs : WiringOutputs).RouteColumn,
            "wiring route plays on records from output device speakers headphones microphone several cable", WiringTab)))
        .Concat(_cards.Values.Select(c => Entry(c, "Combined device", "combine combined speakers microphones members cable edit", WiringTab)));

    /// <param name="openFx">Opens the FX window for a chain key (null = master).</param>
    /// <param name="ask">Shows a setup message window; true when the user confirmed.</param>
    /// <param name="combine">Shows the Combine devices window; true when the user pressed Create.</param>
    public VolumePageViewModel(MixerService mixer, SoundModeService modes, EffectsEngine engine, EngineInstaller installer, HotkeyRegistry hotkeys,
        OsdService osd, Action<string> report, Action<string?> openFx,
        Func<EngineMessage, Task<bool>> ask, Func<CombineDeviceViewModel, Task<bool>> combine)
    {
        _mixer = mixer;
        _modes = modes;
        _engine = engine;
        _installer = installer;
        _ask = ask;
        _combine = combine;
        _osd = osd;
        _report = report;
        _openFx = openFx;
        mixer.Report = report;

        SoundMode = new SoundModeChoice(SetSoundMode, modes.Refresh);
        CombineGate = Gate();
        _soundModeCard = new GalleryItem("Sound mode", SoundModes.All.Select(m => m.Name).ToList(), IndexOf(mixer.SoundMode),
            SoundModePreview, i => SetSoundMode(SoundModes.All[i]));
        _combineCard = new ButtonItem("Combine devices", "Combine…", "Joins speakers or microphones (Full control)",
            () => _ = CombineAsync());
        _master = NewMaster();
        _add = new AddProgramItem { Add = path => { var key = mixer.AddProgram(path); report($"Added {mixer.Find(key)?.Name}"); } };

        Rebuild();
        RefreshInputs();
        SyncModes();
        Tabs.PropertyChanged += OnTabChanged;
        mixer.Updated += Merge;
        modes.Changed += SyncModes;
        engine.StatusChanged += OnEngineStatus;
        _meterTimer.Tick += (_, _) => FeedMeters();
        RegisterHotkeys(hotkeys);
    }

    public override void OnShown()
    {
        _modes.Refresh();
        _mixer.Scan();
        Rebuild();
        RefreshInputs();
        SyncModes();
    }

    private ModeGate Gate() => new(SetSoundMode);

    private void OnTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageTabs.Current)) return;
        OnPropertyChanged(nameof(ShowsMixer));
        OnPropertyChanged(nameof(ShowsWiring));
    }

    /// <summary>The Mixer's mode words: open Wiring, where the mode is chosen.</summary>
    [RelayCommand] private void ShowSoundMode() => ShowWiring(null);

    private static int IndexOf(SoundMode mode) => SoundModes.All.ToList().IndexOf(mode);

    /// <summary>Every Mixer row, in list order.</summary>
    private IEnumerable<VolumeRow> AllRows() => Outputs.Items.OfType<VolumeRow>().Concat(Inputs.Items.OfType<VolumeRow>());

    /// <summary>Every Wiring table row, in list order.</summary>
    private IEnumerable<WiringRow> WiringRows() => WiringOutputs.Items.OfType<WiringRow>().Concat(WiringInputs.Items.OfType<WiringRow>());

    private void UpdateSummaries()
    {
        Outputs.Summary = $"Master + {CountOf(_items.Count, "program")}";
        Inputs.Summary = CountOf(Inputs.Items.OfType<VolumeRow>().Count(), "device");
    }

    /// <summary>"1 device", "3 devices".</summary>
    private static string CountOf(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Drops the rows whose key was not seen in the latest read (a program closed, a device unplugged).</summary>
    private static void RemoveMissing<T>(Dictionary<string, T> rows, HashSet<string> seen) where T : VolumeRow
    {
        foreach (var gone in rows.Keys.Where(k => !seen.Contains(k)).ToList()) rows.Remove(gone);
    }

    public void Dispose()
    {
        Tabs.PropertyChanged -= OnTabChanged;
        _mixer.Updated -= Merge;
        _modes.Changed -= SyncModes;
        _engine.StatusChanged -= OnEngineStatus;
        _meterTimer.Stop();
    }
}
