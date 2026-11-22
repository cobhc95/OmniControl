using OmniControl.App.Features;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Shared.Presets.Manager;

namespace OmniControl.App.Shell;

// The feature switch's share of the shell (FeatureSwitches, App settings > Features): only the modules whose features are on start; a live
// feature (FeatureInfo.Live) switched on starts at once, switched off has its pages closed, its shut-down steps run and what it put in the
// master lists taken out (FeatureScope); any other waits for the next start, and its card says so.
public sealed partial class ShellViewModel
{
    private readonly FeatureSwitches _features;
    private readonly FeatureContext _context;
    private readonly Dictionary<IFeatureModule, Running> _running = new();

    /// A module that runs: what it registered, its pages and the features it started with.
    private sealed record Running(FeatureScope Scope, IReadOnlyList<string> Features)
    {
        public IReadOnlyList<PageSlot> Pages { get; set; } = Array.Empty<PageSlot>();
    }

    /// The module holds a feature that is on (its pages show with only that feature's parts).
    private bool Wanted(IFeatureModule module) => module.Features.Count == 0 || module.Features.Any(_features.IsOn);

    private static bool Live(IFeatureModule module) => module.Features.All(id => FeatureList.Find(id)?.Live == true);

    /// The features the module would run with now.
    private IReadOnlyList<string> WantedFeatures(IFeatureModule module) => module.Features.Where(_features.IsOn).ToList();

    /// Registers the module through a scope of its own.
    private void Begin(IFeatureModule module)
    {
        var running = new Running(new FeatureScope(_context, _hardware.Unregister, _presetGroups.Unregister, _osd.Forget), WantedFeatures(module));
        _running[module] = running;
        module.Register(running.Scope.Context);
        _features.SetRunning(running.Features, true);
    }

    /// Makes the module's sidebar pages; each registers its entries now.
    private void MakePages(IFeatureModule module) => _running[module].Pages = module.Pages.Select(Slot).ToList();

    private IReadOnlyList<PageSlot> RunningPages() => _sidebar.Where(_running.ContainsKey).SelectMany(m => _running[m].Pages).ToList();

    /// The choice changed: each live module starts or stops to match it (a module whose own features changed starts again).
    private void ApplyFeatures()
    {
        var changed = false;
        foreach (var module in _modules.Where(Live))
        {
            var running = _running.GetValueOrDefault(module);
            var wanted = Wanted(module);
            if (running is not null && (!wanted || !running.Features.SequenceEqual(WantedFeatures(module))))
            {
                Stop(module);
                changed = true;
            }
            if (wanted && !_running.ContainsKey(module))
            {
                StartLive(module);
                changed = true;
            }
        }
        if (changed) PagesChanged();
    }

    private void StartLive(IFeatureModule module)
    {
        var name = string.Join(", ", WantedFeatures(module).Select(id => FeatureList.Find(id)?.Name));
        if (Guard.Run($"Feature on: {name}", () =>
            {
                Begin(module);
                MakePages(module);
                module.PagesMade();
            }) is null)
        {
            foreach (var slot in _running[module].Pages) AddRestoreAction(slot);
            return;
        }
        Report(Guard.SeeLog($"{name} couldn't start."));
        if (_running.ContainsKey(module)) Stop(module);
    }

    /// Its pages close (the open one gives the window back to the first page), then its scope stops it.
    private void Stop(IFeatureModule module)
    {
        var running = _running[module];
        _running.Remove(module);
        foreach (var slot in running.Pages)
        {
            if (slot == _openSlot) CloseOpenPage();
            DropRestore(slot);
            if (slot.IfMade is IDisposable page) Guard.Run($"Feature off: {slot.Title} page", page.Dispose);
        }
        running.Scope.Stop();
        _features.SetRunning(running.Features, false);
    }

    private void CloseOpenPage()
    {
        if (_windowShown) _wired?.OnOnScreenChanged(false);
        _wired = null;
        _openSlot!.IsOpen = false;
        _openSlot = null;
        CurrentPage = null;
    }

    /// The sidebar, search, category filter, sections and Restore all follow the pages there are now.
    private void PagesChanged()
    {
        _allPages = RunningPages();
        UseSections();
        RefreshRestoreParts();
        (_pageIndex, _entryIndex) = (null, null);
        Categories = CategoriesNow();
        if (!Categories.Contains(SelectedCategory)) SelectedCategory = AllCategories; // filters again
        ApplyFilter();
    }

    /// The Choose-the-action window's sections are the sidebar pages, in the sidebar's order, with their icons; the preset manager's too.
    private void UseSections()
    {
        _hotkeys.UseSections(AllSlots.Select((p, i) => new ActionSection(p.Title, p.Icon, i + 1)).ToList());
        _presetManager.UseSections(_allPages.Select(p => new PresetSection(p.Title, p.Icon))
            .Append(new PresetSection(ActionsWindowService.Title, ActionsWindowService.Icon)).ToList());
    }
}
