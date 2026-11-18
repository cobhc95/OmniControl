using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>
/// The recording bar: the timer, Pause / Resume, Stop, Cancel, and the microphone and system sound switches (an MP4 only). Esc on the bar stops, twice quickly cancels.
/// The recorder does the work; this only asks it and shows what it says.
/// </summary>
public sealed partial class RecordingViewModel : ObservableObject, IDisposable
{
    private readonly ScreenRecorder _recorder;
    private readonly Action _stop, _cancel;
    private DateTime _lastEsc = DateTime.MinValue;
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

    public RecordingViewModel(ScreenRecorder recorder, Action stop, Action cancel, Action<string> report, bool systemSound, bool microphone)
    {
        _recorder = recorder;
        _stop = stop;
        _cancel = cancel;
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
    private void Cancel() => _cancel();

    /// <summary>Opening or closing a sound capture runs off the UI thread; the switch shows the new state once it is done.</summary>
    [RelayCommand]
    private async Task ToggleMicrophone()
    {
        var on = !Microphone;
        if (await _recorder.SetMicrophoneAsync(on) is { } problem) _report(problem);
        else Microphone = on;
    }

    [RelayCommand]
    private async Task ToggleSystemSound()
    {
        var on = !SystemSound;
        if (await _recorder.SetSystemSoundAsync(on) is { } problem) _report(problem);
        else SystemSound = on;
    }

    /// <summary>The bar's keys: Esc stops. True when the key was used.</summary>
    public bool HandleKey(Key key)
    {
        if (key != Key.Escape) return false;
        var now = DateTime.UtcNow;
        var twice = now - _lastEsc <= Timing.RecordingEscTwice;
        _lastEsc = now;
        if (twice) _cancel(); // Esc twice quickly is the kill switch
        else _stop();
        return true;
    }

    public void Dispose()
    {
        _tick.Stop();
        _tick.Tick -= OnTick;
    }
}
