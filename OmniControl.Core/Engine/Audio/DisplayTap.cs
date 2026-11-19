namespace OmniControl.Core.Engine;

/// One block of what a <see cref="DisplayTap"/> heard: interleaved stereo floats, in the pipe's format. The callee may change it freely (it is a scratch copy).
public delegate void DisplayBlock(Span<float> stereo);

/// A display-only loopback of one playback device (what you hear, after Equalizer APO): records it with the effects pipe's capture and
/// hands every block to a callback on its own thread. It plays nothing and routes nothing anywhere; the callback must only draw from
/// the sound (a meter, an analyser) and throw it away. Stop it with <see cref="Dispose"/>, which ends the thread and releases the capture.
public sealed class DisplayTap : IDisposable
{
    private readonly PipeCapture _capture;
    private readonly DisplayBlock _onBlock;
    private readonly Thread _thread;
    private volatile bool _stopped;

    /// Raised on the tap's thread when it stopped with an error (the device went away); the sound is no longer heard.
    public event Action<string>? Failed;

    private DisplayTap(PipeCapture capture, DisplayBlock onBlock)
    {
        _capture = capture;
        _onBlock = onBlock;
        _capture.Failed += OnCaptureFailed;
        _thread = new Thread(Run) { IsBackground = true, Name = "Display tap" };
        _thread.Start();
    }

    /// Starts hearing <paramref name="playbackDeviceId"/>. Throws (COM or invalid operation) when Windows refuses to record it.
    public static async Task<DisplayTap> StartAsync(string playbackDeviceId, DisplayBlock onBlock)
    {
        var capture = await PipeCapture.StartAsync(PipeSource.Playback(playbackDeviceId)).ConfigureAwait(false);
        return new DisplayTap(capture, onBlock);
    }

    private void OnCaptureFailed(string reason) => Failed?.Invoke(reason);

    private void Run()
    {
        var block = new float[CoreTiming.PipeMaxBlockFrames * CoreTiming.PipeChannels];
        var ring = _capture.Ring;
        try
        {
            while (!_stopped)
            {
                ring.DropTo(CoreTiming.PipeSamplesPerMs * CoreTiming.PipeMaxQueueMs); // never fall behind the sound being heard
                var samples = Math.Min(ring.Count, block.Length) / CoreTiming.PipeChannels * CoreTiming.PipeChannels;
                if (samples == 0)
                {
                    Thread.Sleep(CoreTiming.PipeCaptureBufferMs / 2);
                    continue;
                }
                var part = block.AsSpan(0, samples);
                part.Clear();
                ring.MixInto(part);
                _onBlock(part);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException or ArgumentException)
        {
            // The callback (a plug-in) refused the sound: the display stops and says why, nothing else is affected.
            Failed?.Invoke(ex.Message);
        }
    }

    public void Dispose()
    {
        _stopped = true;
        _capture.Failed -= OnCaptureFailed;
        if (Thread.CurrentThread != _thread) _thread.Join(CoreTiming.DisplayTapStopWaitMs);
        _capture.Dispose();
    }
}
