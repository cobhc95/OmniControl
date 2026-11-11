using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniDial.Core;
using OmniDial.Core.Interfaces;
using OmniDial.Core.Models;

namespace OmniDial.Avalonia.ViewModels;

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

    public IMonitor? SelectedRaw => SelectedMonitor?.Raw;

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
        SelectedMonitor = Monitors.Count > 0 ? Monitors[0] : null;
        AddLog($"Found {_engine.Monitors.Count} DDC/CI monitor(s).");
        LoadSelected();
    }

    [RelayCommand]
    public void RunCommand()
    {
        if (string.IsNullOrWhiteSpace(CommandText)) return;
        var r = _engine.ExecuteCommand(CommandText, AddLog);
        AddLog(r.Success ? "→ OK" : $"→ FAIL: {r.Message}");
        StatusText = r.Success ? "OK" : r.Message;
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

    partial void OnSelectedMonitorChanged(MonitorRow? value) => LoadSelected();

    partial void OnBrightnessChanged(int value) { ApplySlider("b", value); }
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
        var mon = SelectedRaw;
        if (mon == null) return;
        var b = mon.GetVcp(VcpCode.Brightness);
        if (b is { } bv) Brightness = (int)Math.Clamp(bv.Current, 0, 100);
        var c = mon.GetVcp(VcpCode.Contrast);
        if (c is { } cv) Contrast = (int)Math.Clamp(cv.Current, 0, 100);
        var v = mon.GetVcp(VcpCode.Volume);
        if (v is { } vv) Volume = (int)Math.Clamp(vv.Current, 0, 100);
    }

    private void ApplySlider(string action, int value)
    {
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

public sealed class MonitorRow(IMonitor mon)
{
    public IMonitor Raw { get; } = mon;
    public string Display { get; } =
        $"{(string.IsNullOrWhiteSpace(mon.Info.Alias) ? mon.Info.DeviceName : mon.Info.Alias)}  —  {Truncate(mon.Info.FriendlyName, 80)} [{mon.Info.DeviceName}]";

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "…" : s;
    public override string ToString() => Display;
}
