using OmniControl.App.Dialogs;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using PresetGroups = OmniControl.App.Presets.PresetGroups;
using OmniControl.App.Services;
using OmniControl.App.States;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Volume page, two tabs, with the Volume preset drop-down and its cogwheel at the right of the tab row. Mixer: Outputs (the master, one row per
/// program, open or remembered, and combined speakers) and Microphones (every recording device and combined microphone); each row
/// in columns: where it plays (the same Plays on menu as Wiring's), how its effects run (the Method chip and menu), pan and volume knobs,
/// mute, meter, and the FX button that opens and closes the row's FX window. Wiring: the cable status, a table of where each source
/// plays or records from and the cable it uses, and the combined devices. Both tabs read the same routing model.
/// Sound state lives in <see cref="MixerService"/>; what can run in <see cref="EffectsSupportService"/>; what runs in
/// <see cref="EffectsEngine"/>. This class turns those into rows and keeps them up to date. Partial files: rows (.Rows), microphones
/// and combined devices (.Inputs), the routing model, the rows' columns and gates (.Routing), the Wiring tab and the Plays on menus
/// (.Wiring), its Virtual cables group (.Cables), the install steps (.Steps), the presets (.Presets), combining (.Combine), hotkeys and meters (.Hotkeys).
/// </summary>
public sealed partial class VolumePageViewModel : PageViewModel, IDisposable
{
    private readonly MixerService _mixer;
    private readonly StateRegistry _states;
    private readonly EffectsSupportService _support;
    private readonly EffectsEngine _engine;
    private readonly EngineInstaller _installer;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Func<CombineDeviceViewModel, Task<bool>> _combine;
    private readonly OsdService _osd;
    private readonly ProgramDiscovery _discovery = new();
    private readonly Action<string> _report;
    private readonly Action<string?> _openFx;
    private readonly Action<string?> _toggleFx;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _meterTimer = new() { Interval = Timing.MeterFrame };

    public override string Title => "Volume";

    public override string? NavGroup => NavGroups.Devices;

    public override string KeyHelp => string.Join(Environment.NewLine,
        "Ctrl+Tab / Ctrl+Shift+Tab switch between Mixer and Wiring.",
        "Mixer: Left/Right choose a part of a row (Plays on, Method, pan, volume, mute, FX); Enter opens it or starts turning a knob, Enter or Esc ends; Home resets a knob.",
        "Wiring: Enter opens a row's menu; Esc closes it.");
    public override string Icon => "M3,9 H7 L12,4 V20 L7,15 H3 Z M15,8.5 A5,5 0 0 1 15,15.5 L16.2,17 A7,7 0 0 0 16.2,7 Z";
    public override string Keywords => "volume sound audio mixer wiring master apps programs pan balance mute fx effects chain equaliser eq compressor plays on output device microphone combine method cable preset presets";

    /// <summary>The page's tabs (their names are also the search groups of what each shows).</summary>
    private const string MixerTab = "Mixer", WiringTab = "Wiring";

    /// <summary>The words of the column that says where rows play or record from (both tabs) and of the Plays on menu's title.</summary>
    private const string PlaysOnWords = "Plays on", RecordsFromWords = "Records from";

    public PageTabs Tabs { get; } = new(MixerTab, WiringTab);
    public bool ShowsMixer => Tabs.Current == MixerTab;
    public bool ShowsWiring => Tabs.Current == WiringTab;

    /// <summary>Outputs: the master, programs and combined speakers; "Show closed" and "Add program" at the right of its header. Open unless the user closed it.</summary>
    public VolumeGroup Outputs { get; } = new("Volume/Outputs", "Outputs")
    {
        ExpandedByDefault = true,
        SummaryBesideName = true,
        RouteColumn = PlaysOnWords,
        ShowsPan = true,
        Tip = "Plays on and Method open their menus; FX opens and closes the row's FX window. Programs with settings are remembered after they close; × forgets one.",
    };

    /// <summary>Microphones: recording devices and combined microphones. Open unless the user closed it (key kept from "Inputs").</summary>
    public VolumeGroup Inputs { get; } = new("Volume/Inputs", "Microphones") { ExpandedByDefault = true, RouteColumn = RecordsFromWords };

    /// <summary>Whether new combined speakers can be created and run now (no cable or none free): the "+ Combine speakers" pill and the window's choice.</summary>
    public ControlGate CombineSpeakersGate { get; }

    /// <summary>The same for a new combined microphone (needs a free cable apps can record from).</summary>
    public ControlGate CombineMicrophonesGate { get; }

    /// <summary>The Wiring tab's cable chip: The cable status line ("Ready · 1 virtual cable", "Needs VB-CABLE"); null before the first look.</summary>
    [ObservableProperty] private TagItem? _cableStatus;

    /// <summary>Install VB-CABLE beside the chip while none is installed; null = nothing to install.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCableSetup))] private CardAction? _cableSetup;
    public bool HasCableSetup => CableSetup is not null;

    /// <summary>The search card for combining, next to every row.</summary>
    private readonly ButtonItem _combineCard;

    public override IEnumerable<SearchEntry> SearchEntries =>
        new SearchEntry[]
        {
            Entry(_combineCard, "Combine devices", "combine merge speakers microphones mic mics mix several devices together cable stereo mix", WiringTab),
        }
        .Concat(AllRows().Select(r => Entry(r, Title, "sound audio mixer app program pan fx effects method cable capture replay recording output device microphone", MixerTab)))
        .Concat(WiringRows().Select(w => Entry(w, (WiringInputs.Items.Contains(w) ? WiringInputs : WiringOutputs).RouteColumn,
            "wiring route plays on records from output device speakers headphones microphone combine combined mix mics several cable", WiringTab)))
        .Concat(CableEntries());

    /// <param name="openFx">Opens the FX window for a chain key (null = master): the Easy view's effects.</param>
    /// <param name="toggleFx">Opens the FX window for a chain key, or closes it when it is open: a Mixer row's FX button.</param>
    /// <param name="ask">Shows a setup message window; true when the user confirmed.</param>
    /// <param name="combine">Shows the Combine devices window; true when the user pressed Create.</param>
    /// <param name="presetGroups">The master list of preset groups (the Volume presets register there).</param>
    /// <param name="openPresets">Opens the preset manager at a section and group (the cogwheel).</param>
    /// <param name="states">The master list of states: the master's volume and mute and the default speaker are registered there.</param>
    /// <remarks>
    /// Creation order (an earlier start-up crash came from a piece used before it was made): first the services and callbacks handed in;
    /// then the gates and the Wiring groups (they
    /// use only the gates); the search cards; the master row (its menus reach the page only when they open); the program list's tools;
    /// and only then the first reads (Rebuild, RefreshInputs, SyncModes), the event subscriptions, the hotkeys and the Easy view, which
    /// wraps the rows. Every step reads only what an earlier step made; <c>VolumeConstructionTests</c> builds the pieces in this order
    /// with no devices and no programs.
    /// </remarks>
    public VolumePageViewModel(MixerService mixer, EffectsSupportService support, EffectsEngine engine, EngineInstaller installer, HotkeyRegistry hotkeys,
        OsdService osd, Action<string> report, Action<string?> openFx, Action<string?> toggleFx,
        Func<DialogMessage, Task<bool>> ask, Func<CombineDeviceViewModel, Task<bool>> combine, VolumePresets presets,
        PresetGroups presetGroups, Action<string?, string?> openPresets, StateRegistry states)
    {
        _mixer = mixer;
        _states = states;
        _support = support;
        _engine = engine;
        _installer = installer;
        _ask = ask;
        _combine = combine;
        _osd = osd;
        _report = report;
        _openFx = openFx;
        _toggleFx = toggleFx;
        _presets = presets;
        _openPresets = openPresets;

        CombineSpeakersGate = new ControlGate();
        CombineMicrophonesGate = new ControlGate();
        WiringOutputs = NewWiringOutputs();
        WiringInputs = NewWiringInputs();
        WiringCables = NewWiringCables();
        _combineCard = new ButtonItem("Combine devices", "Combine…", "Joins speakers or microphones into one",
            () => _ = CombineAsync(null, Array.Empty<string>()));
        _master = NewMaster();
        var addMenu = new AddProgramMenuViewModel(mixer, _discovery, AddProgram);
        Outputs.ProgramTools = new ProgramListTools(addMenu, Rebuild);
        _discovery.Refresh();

        Rebuild();
        RefreshInputs();
        SyncSupport();
        Tabs.PropertyChanged += OnTabChanged;
        mixer.Updated += Merge;
        support.Changed += SyncSupport;
        engine.StatusChanged += OnEngineStatus;
        mixer.MasterDeviceChanged += OnMasterDeviceChanged;
        engine.PlanChanged += OnPlanChanged;
        _meterTimer.Tick += (_, _) => FeedMeters();
        RegisterHotkeys(hotkeys);
        RegisterPresets(presetGroups);
        CreateEasy(hotkeys);
        RegisterStates();
    }

    public override void OnShown()
    {
        _support.Refresh();
        _mixer.Scan();
        Rebuild();
        RefreshInputs();
        SyncSupport();
    }

    private void OnTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageTabs.Current)) return;
        OnPropertyChanged(nameof(ShowsMixer));
        OnPropertyChanged(nameof(ShowsWiring));
    }

    /// <summary>Every Mixer row, in list order.</summary>
    private IEnumerable<VolumeRow> AllRows() => Outputs.Items.OfType<VolumeRow>().Concat(_items.Values).Distinct().Concat(Inputs.Items.OfType<VolumeRow>());

    /// <summary>Every Wiring table row, in list order.</summary>
    private IEnumerable<WiringRow> WiringRows() => WiringOutputs.Items.OfType<WiringRow>().Concat(WiringInputs.Items.OfType<WiringRow>());

    private void UpdateSummaries()
    {
        Outputs.Summary = $"Master + {CountOf(_listed.Count, "program")}";
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
        Outputs.ProgramTools?.Add.Dispose();
        _discovery.Dispose();
        Tabs.PropertyChanged -= OnTabChanged;
        _mixer.Updated -= Merge;
        _support.Changed -= SyncSupport;
        _engine.StatusChanged -= OnEngineStatus;
        _mixer.MasterDeviceChanged -= OnMasterDeviceChanged;
        _engine.PlanChanged -= OnPlanChanged;
        _presetChoice?.Dispose();
        Easy.Dispose();
        ForgetStates();
        _meterTimer.Stop();
    }
}
