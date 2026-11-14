using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// "Test effects on this program": checks Capture and replay on demand from a program row (either mode). It records the program,
/// silences the original each way in turn (muting the session, volume 0, then turned down to <see cref="QuietLevel.Scalar"/> with
/// the recording made up) and checks whether the recording keeps sound (<see cref="QuietLevel.KeepsSound"/>); on the first that
/// works it plays the processed sound for a moment and measures the delay. Whatever happens, the
/// original's own volume, mute and pan are put back before it returns (the journal covers a crash in between).
/// </summary>
internal sealed class CaptureTest
{
    private readonly MixerService _mixer;
    private readonly EngineJournal _journal;
    private readonly DspChain _chain;

    public CaptureTest(MixerService mixer, EngineJournal journal, DspChain chain)
    {
        _mixer = mixer;
        _journal = journal;
        _chain = chain;
    }

    /// <summary>The result to store (<see cref="CaptureTestResult"/>; unchanged when the test could not decide) and what to show (a short line and the full sentence).</summary>
    public async Task<(string Result, TestOutcome Outcome)> RunAsync(string key)
    {
        if (_mixer.Find(key) is not { } entry) return (Unchanged, SoundModes.TestNotListed());
        var streams = _mixer.OpenStreams(key);
        if (streams.Count == 0) return (Unchanged, SoundModes.TestNotPlaying(entry.Name));

        var level = new UserLevel();
        level.Set(entry.Volume, entry.Pan, entry.Muted);
        var processor = new ChainProcessor(level, gateOpen: false);
        processor.SetChain(_chain);
        AudioPipe pipe;
        var roots = ProcessTree.Roots(streams.Select(s => s.ProcessId));
        var sources = roots.Count;
        try
        {
            pipe = await Task.Run(() => AudioPipe.StartAsync(roots.Select(PipeSource.Program).ToList(), new string?[] { streams[0].DeviceId }, processor));
        }
        catch (InvalidOperationException ex)
        {
            return (Unchanged, SoundModes.TestCannotRecord(entry.Name, ex.Message));
        }

        try
        {
            pipe.TakeCapturePeak();
            await Task.Delay(Timing.CaptureTestListen);
            if (pipe.TakeCapturePeak() < ProgramEngine.Audible)
                return (Unchanged, SoundModes.TestHeardNothing(entry.Name));

            // Silent ways first (nothing of the original is left); the quiet way works where the recording follows the session's volume.
            foreach (var method in new[] { SilenceMethod.Mute, SilenceMethod.ZeroVolume, SilenceMethod.Quiet })
            {
                if (!await KeepsSoundWhenSilenced(key, entry, method, pipe, sources, processor)) continue;
                processor.GateOpen = true;
                _journal.Note(key, entry, _mixer.DirectRouteOf(key), silenced: true, routed: false);
                _mixer.Silence(key, method);
                SetMakeup(pipe, sources, method);
                await Task.Delay(Timing.CaptureTestPlay);
                var delay = StableDelay.Round(pipe.DelayMs);
                Restore(key);
                return (CaptureTestResult.From(method), SoundModes.TestPassed(delay, method, _chain.StageCount, EngineStatus.SkippedNote(_chain.Skipped)));
            }
            return (CaptureTestResult.Failed, SoundModes.TestWentSilent(entry.Name));
        }
        finally
        {
            Restore(key);
            pipe.Dispose();
        }
    }

    private static void SetMakeup(AudioPipe pipe, int sources, SilenceMethod method)
    {
        for (var i = 0; i < sources; i++) pipe.SetSourceGain(i, QuietLevel.MakeupFor(method), QuietLevel.CeilingFor(method));
    }

    /// <summary>Silences the original one way and listens: true when the recording still delivers sound after the mute settled.</summary>
    private async Task<bool> KeepsSoundWhenSilenced(string key, AppAudioEntry entry, SilenceMethod method, AudioPipe pipe, int sources, ChainProcessor processor)
    {
        processor.GateOpen = false;
        SetMakeup(pipe, sources, SilenceMethod.Mute); // plain gain while nothing plays
        _journal.Note(key, entry, _mixer.DirectRouteOf(key), silenced: true, routed: false);
        _mixer.Silence(key, method);
        try
        {
            await Task.Delay(Timing.CaptureSilenceSettle);
            pipe.TakeCapturePeak();
            await Task.Delay(Timing.CaptureSilenceProbe - Timing.CaptureSilenceSettle);
            return QuietLevel.KeepsSound(method, pipe.TakeCapturePeak());
        }
        finally
        {
            Restore(key);
        }
    }

    private void Restore(string key)
    {
        if (_mixer.Unsilence(key)) _journal.Forget(key);
    }

    /// <summary>A test that could not decide keeps the earlier result.</summary>
    private string Unchanged => _mixer.Settings.CaptureSilence;
}
