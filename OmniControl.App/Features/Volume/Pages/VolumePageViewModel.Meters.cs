namespace OmniControl.App.Features.Volume.Pages;

/// The level meters.
public sealed partial class VolumePageViewModel
{
    /// The page's view calls these as it is attached and detached. The meters (and the quiet captures behind the microphone meters, the
    /// mixer's close watch and the fine timers) run only while the view is attached AND the page is on screen (the window shown, not
    /// minimised); on the Mixer tab only for groups that are open.
    public void StartMeters()
    {
        _viewAttached = true;
        SyncLive();
    }

    public void StopMeters()
    {
        _viewAttached = false;
        SyncLive();
    }

    public override void OnOnScreenChanged(bool onScreen)
    {
        _onScreen = onScreen;
        SyncLive();
    }

    private bool _viewAttached, _onScreen;
    private IDisposable? _closeWatch;

    private void SyncLive()
    {
        var live = _viewAttached && _onScreen;
        if (live && _closeWatch is null) _closeWatch = _mixer.WatchClosely();
        else if (!live && _closeWatch is not null)
        {
            _closeWatch.Dispose();
            _closeWatch = null;
        }
        var meters = live && ShowsMixer && !IsEasy;
        if (meters == _meterTimer.IsEnabled) return;
        if (meters)
        {
            _meterTimer.Start();
            return;
        }
        _meterTimer.Stop();
        _captures.Keep(Array.Empty<string>());
        foreach (var row in AllRows()) row.Meter.Clear();
    }

    private void FeedMeters()
    {
        if (!ShowsMixer || IsEasy) return;
        var now = _clock.Elapsed.TotalMilliseconds;
        var quiet = true;
        if (Outputs.IsExpanded)
        {
            if (_mixer.Peaks(null, out var ml, out var mr)) _master.Meter.Feed(ml, mr, now);
            quiet &= _master.Meter.IsSilent;
            foreach (var row in _items.Values.Where(i => i.IsOpen))
            {
                _mixer.Peaks(row.Key, out var l, out var r);
                row.Meter.Feed(l, r, now);
                quiet &= row.Meter.IsSilent;
            }
        }
        // A recording device's meter only moves while something captures from it, so the open input rows keep a quiet capture
        // ("Use microphones" on only: the capture lights Windows' microphone-in-use icon).
        var micMeters = Inputs.IsExpanded && _mixer.MicrophonesOn;
        _captures.Keep(micMeters ? _inputs.Keys : Array.Empty<string>());
        if (micMeters)
        {
            foreach (var row in _inputs.Values)
            {
                _mixer.InputPeaks(row.Id, out var l, out var r);
                row.Meter.Feed(l, r, now); // a muted mic still shows what it hears
                quiet &= row.Meter.IsSilent;
            }
        }
        // No event says when sound starts (the levels are read, not pushed), so while every meter is at silence the reads slow down.
        var frame = quiet ? Timing.MeterQuietFrame : Timing.MeterFrame;
        if (_meterTimer.Interval != frame) _meterTimer.Interval = frame;
    }
}
