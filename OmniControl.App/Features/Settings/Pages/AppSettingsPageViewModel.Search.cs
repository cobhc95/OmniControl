using OmniControl.App.Features.Tray;

namespace OmniControl.App.Features.Settings.Pages;

// What App settings offers to global search: each card's words once (its label is the card's), read by the page's results and, without
// making the page, by its word source (Words).
public sealed partial class AppSettingsPageViewModel
{
    private const string ConfirmationKeywords = "confirm confirmation ask again don't dont prompt question reset";
    private const string UpdateKeywords = "update updates upgrade new version release github check latest download";
    private const string FolderLabel = "Scanned folder";

    private static readonly SearchCard ThemeCard = new("Theme", "light dark color mode system", Title: AppearanceName),
        AccentCard = new("Accent colour", "accent color colour highlight orange blue green white windows follow system custom picker hex hue saturation brightness", Title: AppearanceName),
        IntensityCard = new("Accent intensity", "accent intensity strength softer stronger lighter darker shade vivid", Title: AppearanceName),
        BackgroundCard = new("Background colour", "background colour color surface carbon slate blue black oled plum dark grey gray windows follow", Title: AppearanceName),
        TrayIconCard = new(TrayIconChoices.IconWords, "tray icon notification area logo outline sliders command key look choose", Title: AppearanceName),
        TrayColourCard = new(TrayIconChoices.ColourWords, "tray icon colour color white black follow windows taskbar accent", Title: AppearanceName),
        GpuCard = new("Use GPU drawing", "gpu graphics card hardware acceleration drawing rendering smooth scrolling animations memory", Title: AppearanceName),
        GroupsOpenCard = new("Groups when a page opens", "groups open collapsed collapse expand remember last state sections panels page opens", Title: AppearanceName),
        StartWithWindowsCard = new("Start with Windows", "start boot login windows launch", Title: StartName),
        StartMinimisedCard = new("Start minimised", "start minimized hidden background tray", Title: StartName),
        WhenClosingCard = new("When closing", "close tray exit quit notification area minimize x button ask remember", Title: StartName),
        HardwareCheckCard = new("Check hardware every", "hardware check refresh monitors displays sound devices touchpad undetected missing remote desktop safety periodic every minutes", Title: StartName),
        UiReleaseCard = new("Free memory after closing", "free memory ram release closing closed window hidden tray reopen instantly keep ready seconds minutes never let go", Title: StartName),
        FreePagesCard = new("Free pages you leave", "free pages leave left memory ram release rebuild built visited go back slow older pc", Title: StartName),
        PresetsCard = new("Presets", "presets manage layers display monitor equaliser compressor shortcut order rename", Title: DataName),
        BackupCard = new("Backup and restore", "backup restore export import save load move computer wipe reset factory delete all settings", Title: DataName),
        RestoreAllCard = new("Restore all defaults", "reset default restore every page all settings", Title: DataName),
        NetworkServiceCard = new("Network service", "network service remove uninstall per app limits administrator admin", Title: DataName),
        ImportFromPcCard = new("Import from another PC", "import settings another pc computer laptop copy paired other pcs move", Title: DataName),
        ReadyMadeCard = new("Ready-made", "ready-made features everything sound only monitoring only none custom choice", Title: FeaturesName),
        ResetConfirmationsCard = new("Confirmations", ConfirmationKeywords + " all", Title: DataName),
        AddFolderCard = new("Plug-in folders", "plug-in plugin vst vst2 vst3 folder directory path add other places", Title: PluginsName),
        ScanCard = new("Plug-in scan", "plug-in plugin vst vst2 vst3 scan search find refresh", Title: PluginsName),
        KeepFxSearchCard = new(Features.Volume.Audio.EffectsRules.KeepInPlace, "equalizer apo config effects keep in place restore put back include editor", Title: PluginsName),
        UpdateDailyCard = new("Check once a day", UpdateKeywords, Title: UpdatesName),
        VersionCard = new("Version", UpdateKeywords + " current installed", Title: UpdatesName),
        LatestCard = new("Latest release", UpdateKeywords + " pre-release open", Title: UpdatesName),
        AboutCard = new("About and credits", "about credits licence license gpl open source version notice", Title: UpdatesName);

    private static SearchCard FeatureCard(FeatureInfo feature) => new(feature.Name, $"feature features turn on off disable enable run {feature.Description}", Title: FeaturesName);

    private static SearchCard ConfirmationCard(string name) => new(name, ConfirmationKeywords, Title: DataName);

    private static SearchCard FolderCard(string folder) => new(FolderLabel, $"plug-in folder remove {folder}", Title: PluginsName);

    public override IEnumerable<SearchEntry> SearchEntries => new[]
    {
        Entry(Theme, ThemeCard), Entry(Accent, AccentCard), Entry(_accentIntensity, IntensityCard), Entry(Background, BackgroundCard),
        Entry(_trayIcon, TrayIconCard), Entry(_trayColour, TrayColourCard), Entry(_gpuDrawing, GpuCard), Entry(_groupsOpen, GroupsOpenCard),
        Entry(_startWithWindows, StartWithWindowsCard), Entry(_startMinimised, StartMinimisedCard), Entry(_whenClosing, WhenClosingCard),
        Entry(_hardwareCheck, HardwareCheckCard), Entry(_uiRelease, UiReleaseCard), Entry(_freePages, FreePagesCard), Entry(_presets, PresetsCard), Entry(_backup, BackupCard), Entry(_restoreAllCard, RestoreAllCard),
        Entry(_networkService, NetworkServiceCard),
    }.Concat(DataItems.Contains(_importFromPc) ? new[] { Entry(_importFromPc, ImportFromPcCard) } : Array.Empty<SearchEntry>())
     .Concat(_featureCards.Select(c => Entry(c.Card, FeatureCard(c.Feature)))).Append(Entry(ReadyMade, ReadyMadeCard))
     .Append(Entry(_resetAllConfirmations, ResetConfirmationsCard)).Concat(_confirmationRows.Select(row => Entry(row, ConfirmationCard(row.Label))))
     .Concat(_keepFx is null ? Array.Empty<SearchEntry>() : new[] { Entry(_addFolder, AddFolderCard), Entry(_scan, ScanCard), Entry(_keepFx, KeepFxSearchCard) }
        .Concat(PluginItems.OfType<ButtonItem>().Where(IsFolderRow).Select(row => Entry(row, FolderCard(row.Description)))))
     .Concat(new[] { Entry(_updateDaily, UpdateDailyCard), Entry(_version, VersionCard), Entry(_latest, LatestCard), Entry(_about, AboutCard) });

    /// The page's search words (<c>FeaturePage.Words</c>): its cards, the features, the quiet prompts and the plug-in folders (the Plug-ins
    /// cards are words even while Sound and effects is off: a match then only makes the page).
    public static IEnumerable<string> Words(ConfirmationMemory? confirmations, AppSettings? settings) =>
        new[]
        {
            ThemeCard, AccentCard, IntensityCard, BackgroundCard, TrayIconCard, TrayColourCard, GpuCard, GroupsOpenCard, StartWithWindowsCard,
            StartMinimisedCard, WhenClosingCard, HardwareCheckCard, UiReleaseCard, FreePagesCard, PresetsCard, BackupCard, RestoreAllCard, NetworkServiceCard, ImportFromPcCard,
            ReadyMadeCard, ResetConfirmationsCard, AddFolderCard, ScanCard, KeepFxSearchCard, UpdateDailyCard, VersionCard, LatestCard, AboutCard,
        }
        .Concat(FeatureList.All.Select(FeatureCard))
        .Concat(confirmations?.Items.Select(c => ConfirmationCard(c.Value)) ?? Array.Empty<SearchCard>())
        .Concat(settings?.PluginFolders.Select(FolderCard) ?? Array.Empty<SearchCard>())
        .Select(c => c.Words).ToList();

    private static bool IsFolderRow(ButtonItem item) => item.Label == FolderLabel;
}
