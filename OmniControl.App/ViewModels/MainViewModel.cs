using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;

namespace OmniControl.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IMonitorEngine _engine;
    private bool _disposed;

    [ObservableProperty] private ObservableCollection<MonitorRow> _monitors = new();
    [ObservableProperty] private MonitorRow? _selectedMonitor;
    [ObservableProperty] private int _brightness = 50;
    [ObservableProperty] private int _contrast = 50;
    [ObservableProperty] private int _volume = 30;
    [ObservableProperty] private string _commandText = "";
    [ObservableProperty] private ObservableCollection<string> _logLines = new();
    [ObservableProperty] private string _statusText = "Starting…";
    [ObservableProperty] private string _selectedInput = "DP 1";
    [ObservableProperty] private int _laptopBrightness = 50;
    private bool _suppressLaptopSync;

    public string Subtitle => $"DDC/CI monitor control · {_engine.Monitors.Count} external monitors · laptop panel";
    public string SelectedTitle => SelectedMonitor?.Title ?? "No monitor";
    public string SelectedAlias => SelectedRaw != null ? SelectedRaw.Info.Alias : (SelectedMonitor?.Title ?? "—");
    public IMonitorEngine Engine => _engine;
    public bool IsHdmi1Selected => SelectedInput == "HDMI 1";
    public bool IsHdmi2Selected => SelectedInput == "HDMI 2";
    public bool IsDp1Selected => SelectedInput == "DP 1";
    public bool IsUsbCSelected => SelectedInput == "USB-C";
    public IMonitor? SelectedRaw=> SelectedMonitor?.Raw;
    public bool IsLaptopSelected => SelectedMonitor?.IsLaptop == true;
    public bool IsDdcSelected => !IsLaptopSelected;

    public MainViewModel() : this(new MonitorEngine())
    {
    }

    public MainViewModel(IMonitorEngine engine)
    {
        _engine = engine;
        Refresh();
        AddLog($"Engine ready. Monitors: {_engine.Monitors.Count}. Laptop LCD: {_engine.Laptop.IsAvailable}. GPU: {_engine.Gpu.Driver} ({(_engine.Gpu.IsAvailable ? "available" : "not detected")}).");
        AddLog("Try:  ALL b 40   |   <ALIAS> c -5   |   LCD b 70   |   SYS v 30");
    }

    [RelayCommand]
    private void Refresh()
    {
        _engine.RefreshMonitors();
        Monitors.Clear();
        foreach (var m in _engine.Monitors) Monitors.Add(new MonitorRow(m));
        if (_engine.Laptop.IsAvailable) Monitors.Add(MonitorRow.CreateLaptop());
        SelectedMonitor = Monitors.Count > 0 ? Monitors[0] : null;
        AddLog($"Found {_engine.Monitors.Count} DDC/CI monitor(s).");
        LoadSelected();
        if (_engine.Laptop.IsAvailable && _engine.Laptop.GetBrightness(out var lvl, out var max).Success && max > 0)
        {
            _suppressLaptopSync = true;
            LaptopBrightness = (int)Math.Clamp(lvl * 100L / max, 0, 100);
            _suppressLaptopSync = false;
        }
    }

    [RelayCommand]
    public void RunCommand()
    {
        if (string.IsNullOrWhiteSpace(CommandText)) return;
        var r = _engine.ExecuteCommand(CommandText, AddLog);
        AddLog(r.Success ? "→ OK" : $"→ FAIL: {r.Message}");
        StatusText = r.Success ? "OK" : r.Message;
    }

    public void Log(string msg) => Avalonia.Threading.Dispatcher.UIThread.Post(() => AddLog(msg));

    public void RunExternal(string command)
    {
        var r = _engine.ExecuteCommand(command, AddLog);
        AddLog(r.Success ? "→ OK" : $"→ FAIL: {r.Message}");
    }

    [RelayCommand]
    private void PowerOn() => RunForSelected($"p on");
    [RelayCommand]
    private void PowerOff() => RunForSelected($"p off");
    [RelayCommand]
    private void FactoryReset() => RunForSelected($"x 1");

    [RelayCommand]
    private void ReadCapabilities()
    {
        var mon = SelectedRaw;
        if (mon == null) return;
        AddLog(mon.GetCapabilities() ?? "Capabilities string not available.");
    }

    [RelayCommand]
    private void SelectInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        SelectedInput = input;
        var mon = SelectedRaw;
        if (mon == null) { AddLog("No monitor selected."); return; }
        var src = input switch
        {
            "HDMI 1" => VcpCode.Input.Hdmi1,
            "HDMI 2" => VcpCode.Input.Hdmi2,
            "DP 1" => VcpCode.Input.Dp1,
            "USB-C" => VcpCode.Input.UsbC,
            _ => VcpCode.Input.Dp1,
        };
        var r = mon.SetInputSource(src);
        AddLog(r.Success ? $"Input → {input}" : $"Input fail: {r.Message}");
    }

    partial void OnSelectedInputChanged(string value)
    {
        OnPropertyChanged(nameof(IsHdmi1Selected));
        OnPropertyChanged(nameof(IsHdmi2Selected));
        OnPropertyChanged(nameof(IsDp1Selected));
        OnPropertyChanged(nameof(IsUsbCSelected));
    }

    partial void OnSelectedMonitorChanged(MonitorRow? value)
    {
        OnPropertyChanged(nameof(IsLaptopSelected));
        OnPropertyChanged(nameof(IsDdcSelected));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedAlias));
        OnPropertyChanged(nameof(Subtitle));
        LoadSelected();
    }

    partial void OnBrightnessChanged(int value)
    {
        if (IsLaptopSelected && !_suppressLaptopSync)
        {
            _suppressLaptopSync = true;
            LaptopBrightness = value;
            _suppressLaptopSync = false;
        }
        ApplySlider("b", value);
    }

    partial void OnLaptopBrightnessChanged(int value)
    {
        if (_suppressLaptopSync) return;
        try
        {
            if (!_engine.Laptop.IsAvailable) { AddLog("No laptop panel available."); return; }
            if (_engine.Laptop.GetBrightness(out var lvl, out var max).Success && max > 0)
            {
                var v = (int)Math.Clamp(value * max / 100, 0, max);
                if (lvl == v) return;
                var r = _engine.Laptop.SetBrightness(v);
                if (!r.Success) AddLog(r.Message);
            }
        }
        catch { }
    }
    partial void OnContrastChanged(int value) { ApplySlider("c", value); }
    partial void OnVolumeChanged(int value) { ApplySlider("v", value); }

    private void RunForSelected(string suffix)
    {
        var mon = SelectedRaw;
        if (mon == null) { AddLog("No monitor selected."); return; }
        var cmd = $"{mon.Info.Alias} {suffix}";
        var r = _engine.ExecuteCommand(cmd, AddLog);
        AddLog(r.Success ? "→ OK" : $"→ FAIL: {r.Message}");
    }

    private void LoadSelected()
    {
        if (IsLaptopSelected)
        {
            if (_engine.Laptop.GetBrightness(out var lvl, out var max).Success)
            {
                var pct = max > 0 ? (int)Math.Clamp(lvl * 100L / max, 0, 100) : lvl;
                _suppressLaptopSync = true;
                LaptopBrightness = pct;
                _suppressLaptopSync = false;
                Brightness = pct;
            }
            return;
        }
        var mon = SelectedRaw;
        if (mon == null) return;
        var b = mon.GetVcp(VcpCode.Brightness);
        if (b is { } bv) Brightness = (int)Math.Clamp(bv.Current, 0, 100);
        var c = mon.GetVcp(VcpCode.Contrast);
        if (c is { } cv) Contrast = (int)Math.Clamp(cv.Current, 0, 100);
        var v = mon.GetVcp(VcpCode.Volume);
        if (v is { } vv) Volume = (int)Math.Clamp(vv.Current, 0, 100);
        var inp = mon.GetVcp(VcpCode.InputSource);
        if (inp is { } iv) SelectedInput = iv.Current switch
        {
            VcpCode.Input.Hdmi1 => "HDMI 1",
            VcpCode.Input.Hdmi2 => "HDMI 2",
            VcpCode.Input.Dp1 => "DP 1",
            VcpCode.Input.UsbC => "USB-C",
            _ => SelectedInput,
        };
    }

    private void ApplySlider(string action, int value)
    {
        if (IsLaptopSelected)
        {
            if (action != "b") return;
            var lr = _engine.Laptop.SetBrightness(value);
            if (!lr.Success) AddLog(lr.Message);
            return;
        }
        var mon = SelectedRaw;
        if (mon == null) return;
        var r = action switch
        {
            "b" => mon.SetBrightness(value),
            "c" => mon.SetContrast(value),
            "v" => mon.SetVolume(value),
            _ => OperationResult.Ok(),
        };
        if (!r.Success) AddLog(r.Message);
    }

    private void AddLog(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        LogLines.Add(line);
        if (LogLines.Count > 500) LogLines.RemoveAt(0);
        StatusText = msg;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed class MonitorRow
{
    public IMonitor? Raw { get; }
    public bool IsLaptop { get; }
    public string Display { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Badge { get; }
    public IBrush? BadgeBg { get; }
    public IBrush? BadgeFg { get; }

    public MonitorRow(IMonitor mon)
    {
        Raw = mon;
        Title = string.IsNullOrWhiteSpace(mon.Info.Alias) ? mon.Info.DeviceName : mon.Info.Alias;
        Subtitle = $"{Truncate(mon.Info.FriendlyName, 40)} · {mon.Info.DeviceName}";
        Badge = "DDC/CI"; BadgeBg = Res("BrushBadgeBlueBg"); BadgeFg = Res("BrushAccentDark");
        Display = $"{(string.IsNullOrWhiteSpace(mon.Info.Alias) ? mon.Info.DeviceName : mon.Info.Alias)}  —  {Truncate(mon.Info.FriendlyName, 80)} [{mon.Info.DeviceName}]";
    }

    private MonitorRow() { IsLaptop = true; Display = "Laptop panel (WMI)"; Title = "LCD"; Subtitle = "Laptop panel"; Badge = "WMI"; BadgeBg = Res("BrushBadgeGreenBg"); BadgeFg = Res("BrushBadgeGreenFg"); }

    private static IBrush? Res(string key) =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var r) ? r as IBrush : null;
    public static MonitorRow CreateLaptop() => new();

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "…" : s;
    public override string ToString() => Display;
}
