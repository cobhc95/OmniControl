using Avalonia.Controls.Templates;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Fx;
using OmniControl.App.Features.Volume.Fx.Plugins;
using OmniControl.App.Features.Volume.Pages;

namespace OmniControl.App.Features.Volume;

/// Volume: the mixer, the effects engine with its plug-ins, the FX windows, the page's actions and states (<see cref="VolumeControls"/>) and the page.
public sealed class VolumeModule : IFeatureModule
{
    // The effects journal is read first: a previous run that ended without putting programs' sound back is undone before the mixer
    // looks at the programs (field initialisers run in this order, when the module is made).
    private readonly EngineJournal _journal = EngineJournal.Recover();
    private readonly UserPresetStore _presets = UserPresetStore.OnDisk();
    private EffectsSupportService? _support;
    private EffectsEngine? _effects;
    private EngineInstaller? _installer;
    private FxWindowService? _fxWindows;
    private VolumeControls? _controls;
    private EqualizerApoGroup? _apo;
    private VolumePageViewModel? _page; // made at its first use: by the shell, or by an action only the page can do
    private VolumePageSettings? _pageSettings;
    private FeatureContext? _context;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Volume };

    /// The plug-ins found on this PC (App settings scans for them); null while Sound and effects is off.
    public PluginLibrary? Library { get; private set; }

    /// The mixer; null while Sound and effects is off.
    public MixerService? Mixer { get; private set; }

    /// Sound runs through OmniControl's own effects engine now (a blocking memory collection would be heard).
    public bool StreamsAudio => _effects?.Streams == true;

    /// The program paths the mixer remembers (the rule and automation pickers offer them as programs to choose); none while it is off.
    public IEnumerable<string> RememberedPrograms() => Mixer?.RememberedPaths() ?? Enumerable.Empty<string>();

    public void Register(FeatureContext context)
    {
        _context = context;
        var settings = context.Settings;
        var library = Library = new PluginLibrary(settings.Slot(s => s.PluginFolders, (s, folders) => s.PluginFolders = folders));
        var view = new FxViewSettings(settings.Slot(s => s.FxKeepOnTop, (s, on) => s.FxKeepOnTop = on),
            settings.Slot(s => s.FxChainListWidth, (s, width) => s.FxChainListWidth = width), settings.Slot(s => s.EqShowGrid, (s, on) => s.EqShowGrid = on),
            settings.Slot(s => s.EqShowPhase, (s, on) => s.EqShowPhase = on), settings.Slot(s => s.EffectEditors, (s, editors) => s.EffectEditors = editors));
        _pageSettings = new VolumePageSettings(settings.Slot(s => s.VolumeView, (s, shown) => s.VolumeView = shown),
            settings.Slot(s => s.EasyWelcomeDone, (s, done) => s.EasyWelcomeDone = done), settings.Slot(s => s.MixerShowClosed, (s, on) => s.MixerShowClosed = on));
        var mixer = Mixer = new MixerService(context.Hardware, context.Report);
        var support = _support = new EffectsSupportService(new AudioEffectsProbe(), mixer);
        var volumePresets = new VolumePresets(mixer);
        var plugins = new PluginChainHost(mixer.Save);
        var effects = _effects = new EffectsEngine(mixer, support, _journal, plugins, context.Report);
        var displayFeeds = new DisplayFeeds(plugins, effects, context.Report);
        context.Osd.WatchVolumeOf(() => mixer.MasterDeviceId); // the pop-up for outside volume changes follows the master's real device
        mixer.MasterDeviceChanged += context.Osd.RetargetVolume;
        var installer = _installer = new EngineInstaller(support, mixer);
        effects.ApoWanted += () => _ = Guard.RunAsync("Equalizer APO: first use", () => installer.FirstUseAsync(context.AskAsync, context.Report));
        var apo = _apo = new EqualizerApoGroup(support, installer, (progress, work) => EngineInstaller.RunAsync(progress, work, context.Report, context.AskAsync));
        _controls = new VolumeControls(mixer, context.Osd, context.States, context.Actions, context.Presets, volumePresets, context.Report, VolumePageViewModel.Info.Title);
        var fxWindows = _fxWindows = new FxWindowService(mixer, effects, support, plugins, displayFeeds, library, _presets, context.Report, context.OpenPresetManager,
            new FxMixerLinks(ShowMixerFor, apo.DevicesAsync, () => (apo.Devices.Label, apo.Devices.Tip)), context.AskAsync, view);

        context.Imports.OnReload(DataStores.Mixer, mixer.ReloadFromDisk);
        context.Imports.OnReload(DataStores.EffectPresets, _presets.ReloadFromDisk);
        context.Imports.OnReload(DataStores.VolumePresets, volumePresets.ReloadFromDisk);
        context.Shutdown.Add(ShutdownStage.Pages, "volume controls", _controls.Dispose);
        context.Shutdown.Add(ShutdownStage.Pages, "Equalizer APO group", apo.Dispose);
        context.Shutdown.Add(ShutdownStage.OsdLinks, "OSD volume link", () => mixer.MasterDeviceChanged -= context.Osd.RetargetVolume);
        context.Shutdown.Add(ShutdownStage.Effects, "effect windows", fxWindows.CloseAll); // plug-in editors close (and save) before the plug-ins are unloaded
        context.Shutdown.Add(ShutdownStage.Effects, "effects", effects.Dispose); // plug-in settings are saved
        context.Shutdown.Add(ShutdownStage.Mixer, "effects setup", installer.Dispose);
        context.Shutdown.Add(ShutdownStage.Mixer, "effects support", support.Dispose);
        context.Shutdown.Add(ShutdownStage.Mixer, "volume presets", volumePresets.Dispose);
        context.Shutdown.Add(ShutdownStage.Mixer, "mixer", mixer.Dispose);
        context.Shutdown.Add(ShutdownStage.Plugins, "display feeds", displayFeeds.Dispose); // before the plug-ins they read from are unloaded
        context.Shutdown.Add(ShutdownStage.Plugins, "plug-ins", () => { using (ExitGuard.Start(Timing.PluginUnloadLimit)) plugins.Dispose(); });
    }

    private void ShowMixerFor(string deviceId)
    {
        _context!.OpenPage(VolumePageViewModel.Info.Title, null);
        Page().ShowMixerFor(deviceId);
    }

    /// The page, made now when it was not yet: the shell's first use and an action only the page can do get the same one.
    private VolumePageViewModel Page() => _page ??= new VolumePageViewModel(new VolumePageServices(Mixer!, _controls!, _apo!, _support!, _effects!, _installer!,
        _context!.Report, _fxWindows!.Open, _fxWindows.Toggle, _context.AskAsync, CombineWindow.AskAsync, _context.OpenPresetManager, _pageSettings!));

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(VolumePageViewModel.Info, Page)
        {
            Registers = () => _controls!.Register(new VolumeViewCalls(ToggleView, ToggleClosed,
                () => _ = Page().Easy.NextMasterPresetAsync(), () => _ = Page().Easy.RestoreDefaultsAsync())),
        },
    };

    // The page's view and "Show closed" are settings: while the page is not made, the action changes the setting it reads when made.
    private void ToggleView()
    {
        if (_page is { } page) page.ToggleView();
        else _pageSettings!.View.Value = _pageSettings.View.Value == VolumeView.Easy ? VolumeView.Advanced : VolumeView.Easy;
    }

    private void ToggleClosed()
    {
        if (_page is { } page) page.ToggleShowClosed();
        else _pageSettings!.ShowClosed.Value = !_pageSettings.ShowClosed.Value;
    }

    /// The effects' preset groups belong to the Volume page; they are made by the FX windows, which know how a preset reaches an effect.
    public void PagesMade() => _context!.Presets.Register(_fxWindows!.PresetGroupsFor(VolumePageViewModel.Info.Title));

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<VolumePageViewModel, VolumePage>(),
        ViewTemplate.For<VolumeRow, VolumeRowView>(),
        ViewTemplate.For<WiringRow, WiringRowView>(),
        ViewTemplate.For<CableRow, CableRowView>(),
        ViewTemplate.For<EqPanelItem, EqPanelRow>(),
        ViewTemplate.For<EqBandItem, EqBandRow>(),
        ViewTemplate.For<GraphicEqItem, GraphicEqRow>(),
        ViewTemplate.For<GraphicBandItem, GraphicBandRow>(),
        ViewTemplate.For<PluginParamGroup, PluginParamGroupRow>(),
        ViewTemplate.For<DynamicsItem, DynamicsRow>(),
        ViewTemplate.For<IrFileItem, IrFileRow>(),
    };
}
