using Avalonia.Layout;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Icons;
using OmniControl.App.Features.Resources;
using OmniControl.App.Features.Resources.Gadget;
using OmniControl.App.Features.Resources.Pages;
using OmniControl.App.Features.Resources.Weather;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    private static (SamplePreview, GadgetLayouts, ResourcesSettings, WeatherService)? _gadget;

    /// The Resources page's Taskbar gadget tab (GadgetTabView over its real model), under the page's title line and tabs as ResourcesPage.axaml
    /// sets them out. The page's own view-model is not made: its network limits sync Windows' firewall rules and the network service at once.
    /// The gadget's readings are samples: the reader is never asked to read (no counters, no sensor library), the gadget is never put on the
    /// taskbar (it is made hidden and ended at once, before the look is switched on), and the weather has no place, so it never goes online.
    private static void ResourcesGadgetTab(Rig rig, Camera camera)
    {
        Use(ViewTemplate.For<GadgetSettingsModel, GadgetTabView>(), ViewTemplate.For<WeatherPlaceItem, WeatherPlaceRow>());
        var (preview, layouts, settings, weather) = Gadget(rig);
        var model = new GadgetSettingsModel(settings, rig.Actions, preview, layouts, () => { }, weather,
            (_, _) => Task.FromResult<IconPick?>(null), (_, _) => Task.FromResult<PickResult?>(null));
        model.SetOnScreen(true); // as the page does while this tab shows (the preview only draws then)
        var presets = new ProviderChoice("Resources presets", new ResourcePresets(settings), () => { }, withCustom: true);
        var tabs = new PageTabs(ResourcesPageViewModel.ReadingsTab, ResourcesPageViewModel.GadgetTab);
        tabs.Select(ResourcesPageViewModel.GadgetTab);
        var page = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(12, 12, 12, 24),
            Children =
            {
                new PageTitleLine { Title = ResourcesPageViewModel.PageTitle, Right = new PresetPicker { DataContext = presets.Choice } },
                new StackPanel { Spacing = 6, [Grid.IsSharedSizeScopeProperty] = true, Children = { new PageTabsView { DataContext = tabs }, Camera.Build(model) } },
            },
        };
        camera.Sized(new ScrollViewer { Content = page }, "resources-gadget-tab", Camera.PageWidth, Camera.PageHeight, crop: true);
    }

    /// A few ready-made gadget layouts as the taskbar shows them, drawn by the gadget's own cell drawing on the taskbar's colour.
    private static void TaskbarGadgets(Rig rig, Camera camera)
    {
        var (preview, layouts, _, _) = Gadget(rig);
        var strip = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        var surface = Application.Current!.TryFindResource("BrushSidebar", Avalonia.Styling.ThemeVariant.Dark, out var brush) ? brush as IBrush : null;
        foreach (var entry in layouts.Entries.Take(4))
        {
            if (layouts.LookOf(entry.Id) is not { } look) continue;
            var cells = new GadgetCells { HorizontalAlignment = HorizontalAlignment.Left };
            cells.Draw(preview.Cells(look, sampleUnknown: true), GadgetLayout.Of(look));
            strip.Children.Add(new Border
            {
                Background = new SolidColorBrush(preview.Background), Height = 48, Width = 300, Padding = new Thickness(12, 0), CornerRadius = new CornerRadius(4),
                Child = cells,
            });
        }
        camera.Fit(new Border { Background = surface, Child = strip }, "taskbar-gadget");
    }

    private static (SamplePreview, GadgetLayouts, ResourcesSettings, WeatherService) Gadget(Rig rig)
    {
        if (_gadget is { } made) return made;
        const string title = ResourcesPageViewModel.PageTitle;
        var settings = ResourcesSettings.Load();
        settings.Setup.Gadget.Complete();
        settings.Setup.Gadget.Shown = false;
        var monitor = new ResourceMonitor(rig.States, () => TimeSpan.FromSeconds(2), () => null, () => new Dictionary<string, string>(),
            () => throw new IOException("Not read in the screenshot tool."), title);
        var weather = new WeatherService(settings, rig.Actions, rig.States, new MemoryCredentials(), title);
        var layouts = new GadgetLayouts(settings);
        var gadget = new TaskbarGadget(monitor, weather, settings, layouts, rig.Runner, rig.States, Rig.Report, new NoResourcesWindows());
        gadget.Dispose(); // it stops following the settings: switching the look on below never opens it on the taskbar
        settings.Setup.Gadget.Shown = true;
        var preview = new SamplePreview(gadget, monitor, settings);
        _gadget = (preview, layouts, settings, weather);
        return _gadget.Value;
    }

    /// The gadget as the tab previews it: the real gadget's cells with every value a sample; holding the preview asks nothing to read.
    private sealed class SamplePreview(TaskbarGadget gadget, IReadingList readings, ResourcesSettings settings) : IGadgetPreview
    {
        public bool IsShown => settings.Setup.Gadget.Shown;
        public void SetShown(bool on) { }
        public void SetPosition(GadgetPosition position) { }
        public IReadOnlyList<GadgetCell> Cells(GadgetSettings look, bool sampleUnknown = false) => gadget.Cells(look, sampleUnknown: true);
        public Color Background => gadget.Background;
        public Color Ink => gadget.Ink;
        public IDisposable Hold() => new Nothing();
        public event Action? Changed { add { } remove { } }
        public IReadingList Readings => readings;
    }

    private sealed class NoResourcesWindows : IResourcesWindows
    {
        public void OpenApps() { }
        public void OpenGadgetSettings() { }
    }
}
