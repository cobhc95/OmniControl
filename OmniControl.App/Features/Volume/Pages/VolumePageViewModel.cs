using System.Diagnostics;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Pages;

/// The Volume page, two tabs, with the Volume preset drop-down and its cogwheel at the right of the tab row. Mixer: Outputs (the master, one row per
/// program, open or remembered, and combined speakers) and Microphones (every recording device and combined microphone); each row
/// in columns: where it plays (the same Plays on menu as Wiring's), how its effects run (the Method chip and menu), pan and volume knobs,
/// mute, meter, and the FX button that opens and closes the row's FX window. Wiring: the cable status, a table of where each source
/// plays or records from and the cable it uses, and the combined devices. Both tabs read the same routing model.
/// Sound state lives in <see cref="MixerService"/>; what can run in <see cref="EffectsSupportService"/>; what runs in
/// <see cref="EffectsEngine"/>. This class turns those into rows and keeps them up to date. Partial files: rows (.Rows), microphones
/// and combined devices (.Inputs), the routing model, the rows' columns and gates (.Routing), the Wiring tab and the Plays on menus
/// (.Wiring), its Virtual cables group (.Cables) and Equalizer APO group (<see cref="EqualizerApoGroup"/>), the install steps (.Steps), the presets (.Presets), combining (.Combine), the meters (.Meters), Listen (.Listen).
/// Made at its first use: its actions, states and the levels they read are <see cref="VolumeControls"/>, which the module makes at start-up.
public sealed partial class VolumePageViewModel : PageViewModel, IDisposable
{
    private readonly MixerService _mixer;
    private readonly VolumeControls _controls;
    private readonly VolumePageSettings _settings;
    private readonly EffectsSupportService _support;
    private readonly EffectsEngine _engine;
    private readonly EngineInstaller _installer;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Func<CombineDeviceViewModel, Task<bool>> _combine;
    private readonly ProgramDiscovery _discovery = new();
    private readonly ProcessIcons _icons = new();
    private readonly Action<string> _report;
    private readonly Action<string?> _openFx;
    private readonly Action<string?> _toggleFx;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly OmniControl.Core.Engine.CaptureMeterStreams _captures = new(Guard.Log);
    private readonly DispatcherTimer _meterTimer = new() { Interval = Timing.MeterFrame };

    public static readonly PageInfo Info = new("Volume", "M3,9 H7 L12,4 V20 L7,15 H3 Z M15,8.5 A5,5 0 0 1 15,15.5 L16.2,17 A7,7 0 0 0 16.2,7 Z", NavGroups.Devices,
        "volume sound audio mixer wiring master apps programs pan balance mute fx effects chain equaliser eq compressor plays on output device microphone combine method cable preset presets", "volume");

    public override string KeyHelp => string.Join(Environment.NewLine,
        "Ctrl+Tab / Ctrl+Shift+Tab switch between Mixer and Wiring.",
        "Mixer: Left/Right choose a part of a row (Plays on, Method, pan, volume, mute, FX); Enter opens it or starts turning a knob, Enter or Esc ends; Home resets a knob.",
        "Wiring: Enter opens a row's menu; Esc closes it.");

    /// The page's tabs (their names are also the search groups of what each shows).
    private const string MixerTab = "Mixer", WiringTab = "Wiring";

    /// The words of the column that says where rows play or record from (both tabs) and of the Plays on menu's title.
    private const string PlaysOnWords = "Plays on", RecordsFromWords = "Records from";

    public override PageTabs Tabs { get; } = new(MixerTab, WiringTab);
    public bool ShowsMixer => Tabs.Current == MixerTab;
    public bool ShowsWiring => Tabs.Current == WiringTab;

    /// Outputs: the master, programs and combined speakers; "Show closed" and "Add program" at the right of its header. Open unless the user closed it.
    public VolumeGroup Outputs { get; } = new("Volume/Outputs", "Outputs")
    {
        ExpandedByDefault = true,
        SummaryBesideName = true,
        RouteColumn = PlaysOnWords,
        ShowsPan = true,
        Tip = "Plays on and Method open their menus; FX opens and closes the row's FX window. Programs with settings are remembered after they close; × forgets one.",
    };

    /// Microphones: recording devices and combined microphones. Open unless the user closed it (key kept from "Inputs").
    public VolumeGroup Inputs { get; } = new("Volume/Inputs", "Microphones") { ExpandedByDefault = true, RouteColumn = RecordsFromWords, ShowsListen = true };

    /// Whether new combined speakers can be created and run now (no cable or none free): the "+ Combine speakers" pill and the window's choice.
    public ControlGate CombineSpeakersGate { get; }

    /// The same for a new combined microphone (needs a free cable apps can record from).
    public ControlGate CombineMicrophonesGate { get; }

    /// The Wiring tab's cable chip: The cable status line ("Ready · 1 virtual cable", "Needs VB-CABLE"); null before the first look.
    [ObservableProperty] private TagItem? _cableStatus;

    /// Install VB-CABLE beside the chip while none is installed; null = nothing to install.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCableSetup))] private CardAction? _cableSetup;
    public bool HasCableSetup => CableSetup is not null;

    /// The search card for combining, next to every row.
    private readonly ButtonItem _combineCard;

    /// The Wiring tab's Equalizer APO group (set in the constructor).
    public EqualizerApoGroup Apo { get; }

    public override IEnumerable<SearchEntry> SearchEntries =>
        new[] { Entry(_combineCard, CombineCard), Entry(Apo.Status, ApoCard), Entry(UseMicrophones, MicrophonesCard) }
        .Concat(AllRows().Select(r => Entry(r, RowCard(r.Name))))
        .Concat(WiringRows().Select(w => Entry(w, WiringCard(w.Source.Name, WiringInputs.Items.Contains(w)))))
        .Concat(CableEntries());

    private static readonly SearchCard CombineCard = new("Combine devices", "combine merge speakers microphones mic mics mix several devices together cable stereo mix", WiringTab),
        ApoCard = new(EqualizerApoGroup.StatusLabel, "equalizer apo eq effects install remove device selector attach speakers microphone driver", WiringTab, "Equalizer APO"),
        MicrophonesCard = new(EffectsRules.UseMicrophonesLabel, VolumeControls.UseMicrophonesKeywords, MixerTab);

    private static SearchCard RowCard(string name) =>
        new(name, "sound audio mixer app program pan fx effects method cable capture replay recording output device microphone", MixerTab, Info.Title);

    private static SearchCard WiringCard(string name, bool input) => new(name,
        "wiring route plays on records from output device speakers headphones microphone combine combined mix mics several cable", WiringTab, input ? RecordsFromWords : PlaysOnWords);

    /// The page's search words (<c>FeaturePage.Words</c>): its cards, and the rows' names from the mixer (the master, the programs, the
    /// microphones and combined devices, each also a Wiring row) and the cables the effects support found.
    public static IEnumerable<string> Words(MixerService? mixer, EffectsSupportService? support)
    {
        IReadOnlyList<string> names = mixer is null ? Array.Empty<string>() : mixer.Settings.Apps.Select(a => a.Name)
            .Concat(mixer.RecordingDevices.Select(d => d.Name)).Concat(mixer.Combined.Select(c => c.Name)).Prepend(MasterName).Distinct().ToList();
        return new[] { CombineCard.Words, ApoCard.Words, MicrophonesCard.Words }
            .Concat(names.SelectMany(n => new[] { RowCard(n).Words, WiringCard(n, false).Words, WiringCard(n, true).Words }))
            .Concat((support?.Current?.Found ?? Array.Empty<FoundCable>()).Select(f => CableCard(f.Name).Words)).ToList();
    }

    /// <remarks>
    /// Creation order (an earlier start-up crash came from a piece used before it was made): first the services and callbacks handed in;
    /// then the gates and the Wiring groups (they
    /// use only the gates); the search cards; the master row (its menus reach the page only when they open); the program list's tools;
    /// and only then the first reads (Rebuild, RefreshInputs, SyncModes), the event subscriptions, the preset picker and the Easy view, which
    /// wraps the rows. Every step reads only what an earlier step made; <c>VolumeConstructionTests</c> builds the pieces in this order
    /// with no devices and no programs.
    /// </remarks>
    public VolumePageViewModel(VolumePageServices s) : base(Info)
    {
        _settings = s.Settings;
        _isEasy = s.Settings.View.Value == VolumeView.Easy;
        _mixer = s.Mixer;
        _controls = s.Controls;
        _support = s.Support;
        _engine = s.Engine;
        _installer = s.Installer;
        _ask = s.Ask;
        _combine = s.Combine;
        _report = s.Report;
        _openFx = s.OpenFx;
        _toggleFx = s.ToggleFx;
        _openPresets = s.OpenPresets;
        CombineSpeakersGate = new ControlGate();
        CombineMicrophonesGate = new ControlGate();
        UseMicrophones = NewUseMicrophones();
        WiringOutputs = NewWiringOutputs();
        WiringInputs = NewWiringInputs();
        WiringCables = NewWiringCables();
        Apo = s.Apo;
        _combineCard = new ButtonItem(CombineCard.Label, "Combine…", "Joins speakers or microphones into one",
            () => _ = CombineAsync(null, Array.Empty<string>()));
        _master = NewMaster();
        var addMenu = new AddProgramMenuViewModel(_mixer, _discovery, AddProgram);
        Outputs.ProgramTools = new ProgramListTools(addMenu, Rebuild, _settings.ShowClosed); // the installed programs are read when its menu first opens

        Rebuild();
        RefreshInputs();
        SyncSupport();
        Tabs.PropertyChanged += OnTabChanged;
        _mixer.Updated += Merge;
        _support.Changed += SyncSupport;
        _engine.StatusChanged += OnEngineStatus;
        _mixer.MasterDeviceChanged += OnMasterDeviceChanged;
        _engine.PlanChanged += OnPlanChanged;
        _mixer.MicrophonesChanged += OnMicrophonesChanged;
        _mixer.MethodWanted += key => RowOf(key ?? MixerService.MasterKey)?.RequestMethodMenu();
        _meterTimer.Tick += (_, _) => FeedMeters();
        _controls.MasterShown += OnMasterShown;
        _controls.InputShown += OnInputShown;
        _controls.ComboShown += OnComboShown;
        _controls.ListenChanged += OnListenChanged;
        _presetChoice = new ProviderChoice(VolumeControls.PresetsGroupName, _controls.Presets, ManagePresets);
        CreateEasy();
    }

    public override void OnShown()
    {
        _support.Refresh();
        _mixer.ScanAll();
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

    /// Every Mixer row, in list order.
    private IEnumerable<VolumeRow> AllRows() => Outputs.Items.OfType<VolumeRow>().Concat(_items.Values).Distinct().Concat(Inputs.Items.OfType<VolumeRow>());

    /// Every Wiring table row, in list order.
    private IEnumerable<WiringRow> WiringRows() => WiringOutputs.Items.OfType<WiringRow>().Concat(WiringInputs.Items.OfType<WiringRow>());

    private void UpdateSummaries()
    {
        Outputs.Summary = $"Master + {CountOf(_listed.Count, "program")}";
        Inputs.Summary = CountOf(Inputs.Items.OfType<VolumeRow>().Count(), "device");
    }

    /// "1 device", "3 devices".
    private static string CountOf(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// Drops the rows whose key was not seen in the latest read (a program closed, a device unplugged).
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
        _mixer.MicrophonesChanged -= OnMicrophonesChanged;
        _controls.MasterShown -= OnMasterShown;
        _controls.InputShown -= OnInputShown;
        _controls.ComboShown -= OnComboShown;
        _controls.ListenChanged -= OnListenChanged;
        _presetChoice.Dispose();
        Easy.Dispose();
        StopMeters();
    }
}
