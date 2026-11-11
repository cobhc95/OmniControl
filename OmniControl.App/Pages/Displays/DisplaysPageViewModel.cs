using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.Core;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

public sealed partial class DisplaysPageViewModel : PageViewModel
{
    private readonly MonitorEngine _engine;
    private readonly Action<string> _report;

    public override string Title => "Displays";
    public override string Icon => "M4,4 H20 A2,2 0 0 1 22,6 V16 A2,2 0 0 1 20,18 H14 V20 H17 V22 H7 V20 H10 V18 H4 A2,2 0 0 1 2,16 V6 A2,2 0 0 1 4,4 Z M4,6 V16 H20 V6 Z";
    public override string Keywords => "monitor display screen brightness contrast ddc laptop panel";

    public override IEnumerable<SearchEntry> SearchEntries =>
        Displays.SelectMany(d => d.Settings.Select(s => new SearchEntry
        {
            Title = d.Name,
            Category = "Displays",
            Editor = s,
            Keywords = "monitor display screen " + d.Detail,
        }));

    public ObservableCollection<DisplayItem> Displays { get; } = new();

    [ObservableProperty] private DisplayItem? _selected;
    [ObservableProperty] private string _summary = string.Empty;

    public DisplaysPageViewModel(MonitorEngine engine, HotkeyRegistry hotkeys, OsdService osd, Action<string> report)
    {
        _engine = engine;
        _osd = osd;
        _report = report;
        Reload();

        hotkeys.Register(Brightness("displays.brightness.up", "Brightness up", +5));
        hotkeys.Register(Brightness("displays.brightness.down", "Brightness down", -5));
        hotkeys.Register(Contrast("displays.contrast.up", "Contrast up", +5));
        hotkeys.Register(Contrast("displays.contrast.down", "Contrast down", -5));
    }

    private readonly OsdService _osd;
    private const string LaptopId = "laptop";

    private HotkeyAction Brightness(string id, string title, int delta) =>
        new(id, title, "Displays", "monitor display screen brightness",
            ids => StepBrightness(ids, delta), "displays", ListDevices);

    private HotkeyAction Contrast(string id, string title, int delta) =>
        new(id, title, "Displays", "monitor display screen contrast",
            ids => StepContrast(ids, delta), "displays", ListDevices);

    private void StepContrast(IReadOnlyList<string>? ids, int delta)
    {
        int? shown = null;
        foreach (var m in _engine.Monitors.Where(m => ids is null || ids.Contains(m.Info.InstanceId)))
            if (m.GetVcp(VcpCode.Contrast) is { Maximum: > 0 } v)
            {
                var value = Math.Clamp(v.Percent + delta, 0, 100);
                m.SetContrast(value);
                shown ??= value;
            }
        if (shown is { } s) _osd.Show("Contrast", s);
    }

    private IReadOnlyList<HotkeyDevice> ListDevices()
    {
        var list = new List<HotkeyDevice>();
        if (_engine.Laptop.IsAvailable) list.Add(new HotkeyDevice(LaptopId, "Built-in display"));
        list.AddRange(_engine.Monitors.Select(m => new HotkeyDevice(m.Info.InstanceId, m.Info.FriendlyName)));
        return list;
    }

    /// <summary>Changes brightness by <paramref name="delta"/> percent on every display, or only the given ids.</summary>
    private void StepBrightness(IReadOnlyList<string>? ids, int delta)
    {
        int? shown = null;
        if ((ids is null || ids.Contains(LaptopId)) && _engine.Laptop.IsAvailable
            && _engine.Laptop.GetBrightness(out var level, out var max).Success && max > 0)
        {
            shown = Math.Clamp(level * 100 / max + delta, 0, 100);
            _engine.Laptop.SetBrightness(shown.Value);
        }

        foreach (var m in _engine.Monitors.Where(m => ids is null || ids.Contains(m.Info.InstanceId)))
            if (m.GetVcp(VcpCode.Brightness) is { Maximum: > 0 } v)
            {
                var value = Math.Clamp(v.Percent + delta, 0, 100);
                m.SetBrightness(value);
                shown ??= value;
            }
        if (shown is { } s) _osd.Show("Brightness", s);
    }

    public override void OnShown() => Reload();

    /// <summary>Re-detects displays; only controls a display reports are shown.</summary>
    [RelayCommand]
    private void Reload()
    {
        _engine.RefreshMonitors();
        Displays.Clear();

        if (_engine.Laptop.IsAvailable
            && _engine.Laptop.GetBrightness(out var level, out var max).Success && max > 0)
        {
            var laptop = _engine.Laptop;
            var settings = new ObservableCollection<SliderItem>
            {
                new("Brightness", level * 100 / max, v => { _osd.NoteOwnChange(); return laptop.SetBrightness(v); }, _report),
            };
            Displays.Add(new DisplayItem { Name = "Built-in display", Detail = "Laptop panel", Settings = settings });
        }

        foreach (var m in _engine.Monitors)
        {
            var settings = new ObservableCollection<SliderItem>();
            AddVcp(settings, m, "Brightness", VcpCode.Brightness, m.SetBrightness);
            AddVcp(settings, m, "Contrast", VcpCode.Contrast, m.SetContrast);
            Displays.Add(new DisplayItem { Name = m.Info.FriendlyName, Detail = "DDC/CI", Settings = settings });
        }

        Selected = Displays.FirstOrDefault();
        Summary = Displays.Count switch
        {
            0 => "No displays found.",
            1 => "1 display found",
            _ => $"{Displays.Count} displays found",
        };
    }

    private void AddVcp(ObservableCollection<SliderItem> list, IMonitor m, string label,
        byte code, Func<int, OperationResult> set)
    {
        if (m.GetVcp(code) is { Maximum: > 0 } v)
            list.Add(new SliderItem(label, v.Percent, set, _report));
    }
}
