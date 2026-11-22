using OmniControl.App.Features;
using OmniControl.App.Features.OtherPcs.SettingsImport;
using OmniControl.App.Features.Settings.Pages;
using OmniControl.App.Features.Settings.Pages.Backup;
using OmniControl.App.Features.Tray;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Fx.Plugins;
using OmniControl.App.Shared.Presets;
using OmniControl.App.Shared.Presets.Manager;
using OmniControl.App.Shared.Services.Updates;
using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    private static TrayFlyoutModel? _tray;

    /// The tray quick panel's model over the master lists (made before App settings, whose tray icon cards read its look, as in the shell).
    private static TrayFlyoutModel Tray(Rig rig) =>
        _tray ??= new TrayFlyoutModel(rig.Actions, rig.Runner.Run, rig.States, rig.IconPicker, () => { }, _ => { }, () => new Nothing());

    /// App settings. Its one mixer card reads the mixer's saved setting: the mixer is made and ended at once, before its first scan can be
    /// applied (applying is where it would write to programs' sessions), so it only reads Windows once on a worker. Imports from another PC
    /// go through a transport with no PCs, the update check through a fetch that never goes online.
    private static void AppSettingsPage(Rig rig, Camera camera)
    {
        Use(ViewTemplate.For<AppSettingsPageViewModel, AppSettingsPage>());
        var mixer = new MixerService(rig.Hardware, Rig.Report);
        mixer.Dispose();
        var updates = new UpdateService(new UpdateSettings(), () => { }, AppInfo.Version, _ => Task.FromResult("[]"), () => DateTime.Now);
        var backup = new BackupService(_ => Task.FromResult(DialogAnswer.Cancel), Rig.Report, new ImportFromPcService(new NoTransport(), new ImportHooks(), Rig.Report));
        var settings = AppSettings.Current;
        var plugins = new PluginLibrary(settings.Slot(s => s.PluginFolders, (s, folders) => s.PluginFolders = folders));
        var shortcuts = new PresetShortcuts(rig.Actions, rig.Presets, Rig.Report);
        var presets = new PresetManagerService(rig.Presets, shortcuts, new PresetTriggersSlot(shortcuts), Rig.Report);
        var page = new AppSettingsPageViewModel(() => { }, plugins, presets, backup, new ConfirmationMemory(), updates, Rig.Report, mixer, rig.Hardware, settings, Tray(rig), rig.Features);
        rig.Pages.Add(page);
        rig.Restore(page, page.Restorable);
        page.ShowRestoreAll(new PageRestore(new RestoreRunner("all", "All", rig.Restorables, true, _ => Task.FromResult(false), new FileSnapshotStore()), all: true));
        page.Appearance.IsExpanded = true;
        page.StartAndClose.IsExpanded = true;
        camera.Page(page, "app-settings");
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class NoTransport : ISettingsTransport
    {
        public IReadOnlyList<(string Id, string Name)> OnlinePcs() => Array.Empty<(string, string)>();
        public string NameOf(string pcId) => string.Empty;
        public bool IsOnline(string pcId) => false;
        public Task<ImportReply> OffersAsync(string pcId) => throw new InvalidOperationException("No paired PCs.");
        public Task<ImportReply> AreaAsync(string pcId, string area) => throw new InvalidOperationException("No paired PCs.");
    }
}
