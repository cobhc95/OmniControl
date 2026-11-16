using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>
/// The recording bar: the timer, Pause / Resume, Stop, and the microphone and system sound switches (an MP4 only). Esc on the bar stops.
/// The recorder does the work; this only asks it and shows what it says.
/// </summary>
public sealed partial class RecordingViewModel : ObservableObject, IDisposable
{
    private readonly ScreenRecorder _recorder;
    private readonly Action _stop;
    private readonly Action<string> _report;
    private readonly DispatcherTimer _tick = new() { Interval = Timing.RecordingTimerTick };

    [ObservableProperty] private string _time = VideoMath.Clock(TimeSpan.Zero);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PauseTip))] private bool _isPaused;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(MicrophoneTip))] private bool _microphone;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(SystemSoundTip))] private bool _systemSound;

    /// <summary>The file takes sound (MP4); a GIF's bar has no sound switches.</summary>
    public bool TakesSound => _recorder.TakesSound;

    public string PauseTip => IsPaused ? "Resume" : "Pause";
    public string MicrophoneTip => Microphone ? "Microphone: on" : "Microphone: off";
    public string SystemSoundTip => SystemSound ? "System sound: on" : "System sound: off";

    public RecordingViewModel(ScreenRecorder recorder, Action stop, Action<string> report, bool systemSound, bool microphone)
    {
        _recorder = recorder;
        _stop = stop;
        _report = report;
        _systemSound = recorder.TakesSound && systemSound;
        _microphone = recorder.TakesSound && microphone;
        _tick.Tick += OnTick;
        _tick.Start();
    }

    private void OnTick(object? sender, EventArgs e) => Time = VideoMath.Clock(_recorder.Elapsed);

    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused) _recorder.Resume();
        else _recorder.Pause();
        IsPaused = _recorder.IsPaused;
    }

    [RelayCommand]
    private void Stop() => _stop();

    [RelayCommand]
    private void ToggleMicrophone()
    {
        var on = !Microphone;
        if (_recorder.SetMicrophone(on) is { } problem) _report(problem);
        else Microphone = on;
    }

    [RelayCommand]
    private void ToggleSystemSound()
    {
        var on = !SystemSound;
        if (_recorder.SetSystemSound(on) is { } problem) _report(problem);
        else SystemSound = on;
    }

    /// <summary>The bar's keys: Esc stops. True when the key was used.</summary>
    public bool HandleKey(Key key)
    {
        if (key != Key.Escape) return false;
        _stop();
        return true;
    }

    public void Dispose()
    {
        _tick.Stop();
        _tick.Tick -= OnTick;
    }
}
