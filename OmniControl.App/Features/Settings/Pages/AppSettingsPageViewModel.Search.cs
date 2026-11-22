using OmniControl.App.Shared.Presets.Manager;
using OmniControl.App.Features.Tray;

namespace OmniControl.App.Features.Settings.Pages;

// What App settings offers to global search, defined once: each card's words (its label, keywords, choices, menu, tooltip and muted line) are
// a SearchCard the page also makes the card from, and Cards lists them with their live editors, so the page's results and, without making
// the page, its word source (Words) are the same list.
public sealed partial class AppSettingsPageViewModel
{
    private const string ConfirmationKeywords = "confirm confirmation ask again don't dont prompt question reset";
    private const string UpdateKeywords = "update updates upgrade new version release github check latest download";
    private const string FolderLabel = "Scanned folder";

    private static readonly SearchCard ThemeCard = new("Theme", "light dark color mode system", Title: AppearanceName) { Choices = Enum.GetNames<ThemeChoice>() },
        AccentCard = new("Accent colour", "accent color colour highlight orange blue green white windows follow system custom picker hex hue saturation brightness", Title: AppearanceName)
        {
            Choices = AccentService.Choices,
            Menu = new[] { AccentService.CustomEditorLabel },
            Tip = "Used everywhere; the OSD can match it. Windows accent follows the colour chosen in Windows; Custom colour… opens a colour editor.",
        },
        IntensityCard = new("Accent intensity", "accent intensity strength softer stronger lighter darker shade vivid", Title: AppearanceName),
        BackgroundCard = new("Background colour", "background colour color surface carbon slate blue black oled plum dark grey gray windows follow", Title: AppearanceName)
        {
            Choices = SurfaceService.Names,
            Tip = "The colour of pages, cards, windows, pop-downs and the tray panel. Follow Windows uses Windows' own tones.",
        },
        TrayIconCard = new(TrayIconChoices.IconWords, "tray icon notification area logo outline sliders command key look choose", Title: AppearanceName) { Menu = TrayIconChoices.MenuWords },
        TrayColourCard = new(TrayIconChoices.ColourWords, "tray icon colour color white black follow windows taskbar accent", Title: AppearanceName)
        {
            Choices = TrayIconChoices.Colours.Select(c => c.Name).ToList(),
            Tip = "Follow Windows draws an icon white on a dark taskbar and black on a light one; the logo keeps its own tile and dial colours.",
        },
        GpuCard = new("Use GPU drawing", "gpu graphics card hardware acceleration drawing rendering smooth scrolling animations memory", Title: AppearanceName)
        {
            Note = "Smoother scrolling and animations; uses about 40–80 MB more memory.",
        },
        GroupsOpenCard = new("Groups when a page opens", "groups open collapsed collapse expand remember last state sections panels page opens", Title: AppearanceName) { Choices = GroupsOpen.Labels },
        StartWithWindowsCard = new("Start with Windows", "start boot login windows launch", Title: StartName),
        StartMinimisedCard = new("Start minimised", "start minimized hidden background tray", Title: StartName),
        WhenClosingCard = new("When closing", "close tray exit quit notification area minimize x button ask remember", Title: StartName) { Choices = CloseBehaviors.Labels },
        HardwareCheckCard = new("Check hardware every", "hardware check refresh monitors displays sound devices touchpad undetected missing remote desktop safety periodic every minutes", Title: StartName)
        {
            Choices = HardwareChecks.Labels,
        },
        UiReleaseCard = new("Free memory after closing", "free memory ram release closing closed window hidden tray reopen instantly keep ready seconds minutes never let go", Title: StartName)
        {
            Choices = UiReleases.Labels,
            Tip = "How long the closed window stays ready to reopen instantly before OmniControl frees its memory.",
        },
        KeepPagesCard = new("Keep recent pages ready", "keep recent pages ready open instantly fast smooth switch visited go back memory ram built last two", Title: StartName)
        {
            Note = "Pages you go back to open instantly; uses more memory.",
        },
        PresetsCard = new("Presets", "presets manage layers display monitor equaliser compressor shortcut order rename", Title: DataName)
        {
            Note = "Every preset of the program: order, names and shortcuts",
        },
        BackupCard = new("Backup and restore", "backup restore export import save load move computer wipe reset factory delete all settings", Title: DataName)
        {
            Note = "Settings, presets, shortcuts, rules, scripts, custom actions, app lists",
        },
        RestoreAllCard = new("Restore all defaults", "reset default restore every page all settings", Title: DataName)
        {
            Note = "Puts every page back to its defaults after one question; you can undo it",
        },
        NetworkServiceCard = new("Network service", "network service remove uninstall per app limits administrator admin", Title: DataName)
        {
            Note = "Lets per-app network limits work without running OmniControl as administrator",
        },
        ImportFromPcCard = new("Import from another PC", "import settings another pc computer laptop copy paired other pcs move", Title: DataName)
        {
            Tip = "Copy chosen areas of a paired PC's settings here. It has to be on and connected.",
        },
        ReadyMadeCard = new("Ready-made", "ready-made features everything sound only monitoring only none custom choice", Title: FeaturesName)
        {
            Menu = FeatureList.ReadyMade.Select(r => r.Name).Append(FeatureList.Custom).ToList(),
            Tip = "The same choices as Setup's Choose features page.",
        },
        ResetConfirmationsCard = new("Confirmations", ConfirmationKeywords + " all", Title: DataName),
        AddFolderCard = new("Plug-in folders", "plug-in plugin vst vst2 vst3 folder directory path add other places", Title: PluginsName) { Note = "Add a folder to scan" },
        ScanCard = new("Plug-in scan", "plug-in plugin vst vst2 vst3 scan search find refresh", Title: PluginsName) { Note = ScanIdleText },
        KeepFxSearchCard = new(Features.Volume.Audio.EffectsRules.KeepInPlace, "equalizer apo config effects keep in place restore put back include editor", Title: PluginsName)
        {
            Tip = Features.Volume.Audio.EffectsRules.KeepInPlaceTip,
        },
        UpdateDailyCard = new("Check once a day", UpdateKeywords, Title: UpdatesName)
        {
            Tip = "Looks at GitHub's release list for a newer version. It only checks; nothing is downloaded or installed.",
        },
        VersionCard = new("Version", UpdateKeywords + " current installed", Title: UpdatesName),
        LatestCard = new("Latest release", UpdateKeywords + " pre-release open", Title: UpdatesName),
        AboutCard = new("About and credits", "about credits licence license gpl open source version notice", Title: UpdatesName) { Note = "Version, licence, credits" };

    private static SearchCard FeatureCard(FeatureInfo feature) =>
        new(feature.Name, $"feature features turn on off disable enable run {feature.Description}", Title: FeaturesName) { Note = feature.Description };

    private static SearchCard ConfirmationCard(string name) => new(name, ConfirmationKeywords, Title: DataName) { Note = "Asks again before this" };

    private static SearchCard FolderCard(string folder) => new(FolderLabel, $"plug-in folder remove {folder}", Title: PluginsName) { Note = folder };

    protected override SearchBuilder SearchCards => Cards(_confirmations, _settings, _presetManager, this);

    /// THE definition of the page's search: its cards, the features, the quiet prompts and the plug-in folders, each with its live editor when
    /// <paramref name="page"/> is given and has it (the Plug-ins cards are words even while Sound and effects is off: a match then only makes the page).
    private static SearchBuilder Cards(ConfirmationMemory? confirmations, AppSettings? settings, PresetManagerService? presets, AppSettingsPageViewModel? page)
    {
        var plugins = page?._keepFx is not null;
        return new SearchBuilder()
            .Add(ThemeCard, page?.Theme).Add(AccentCard, page?.Accent).Add(IntensityCard, page?._accentIntensity).Add(BackgroundCard, page?.Background)
            .Add(TrayIconCard, page?._trayIcon).Add(TrayColourCard, page?._trayColour).Add(GpuCard, page?._gpuDrawing).Add(GroupsOpenCard, page?._groupsOpen)
            .Add(StartWithWindowsCard, page?._startWithWindows).Add(StartMinimisedCard, page?._startMinimised).Add(WhenClosingCard, page?._whenClosing)
            .Add(HardwareCheckCard, page?._hardwareCheck).Add(UiReleaseCard, page?._uiRelease).Add(KeepPagesCard, page?._keepPages)
            .Add(presets is null ? PresetsCard : PresetsCard with { Choices = presets.Words.Distinct().ToList() }, page?._presets).Add(BackupCard, page?._backup).Add(RestoreAllCard, page?._restoreAllCard).Add(NetworkServiceCard, page?._networkService)
            .Add(ImportFromPcCard, page is not null && page.DataItems.Contains(page._importFromPc) ? page._importFromPc : null)
            .AddEach(FeatureList.All, FeatureCard, f => page?._featureCards.FirstOrDefault(c => c.Feature == f).Card)
            .Add(ReadyMadeCard, page?.ReadyMade)
            .Add(ResetConfirmationsCard, page?._resetAllConfirmations)
            .AddEach(confirmations?.Items ?? Array.Empty<KeyValuePair<string, string>>(), c => ConfirmationCard(c.Value), c => page?._confirmationRows.FirstOrDefault(r => r.Label == c.Value))
            .Add(AddFolderCard, plugins ? page!._addFolder : null).Add(ScanCard, plugins ? page!._scan : null).Add(KeepFxSearchCard, page?._keepFx)
            .AddEach(settings?.PluginFolders ?? new List<string>(), FolderCard,
                folder => plugins ? page!.PluginItems.OfType<ButtonItem>().FirstOrDefault(r => IsFolderRow(r) && r.Description == folder) : null)
            .Add(UpdateDailyCard, page?._updateDaily).Add(VersionCard, page?._version).Add(LatestCard, page?._latest).Add(AboutCard, page?._about);
    }

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page.
    public static IEnumerable<string> Words(ConfirmationMemory? confirmations, AppSettings? settings, PresetManagerService? presets) => Cards(confirmations, settings, presets, null).Words;

    private static bool IsFolderRow(ButtonItem item) => item.Label == FolderLabel;
}
