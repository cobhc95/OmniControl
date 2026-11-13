using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// "Test effects on this program": the owner's spike for Capture and replay, run on demand from a program row's effects (Fast mode). It records the
/// program, mutes the original one way and then the other (muting the session, then volume 0), and checks whether the recording
/// keeps sound each time; on success it plays the processed sound for a moment and measures the delay. Whatever happens, the
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

    /// <summary>The result to store (<see cref="CaptureTestResult"/>; unchanged when the test could not decide) and the sentence to show.</summary>
    public async Task<(string Result, string Message)> RunAsync(string key)
    {
        if (_mixer.Find(key) is not { } entry) return (Unchanged, "This program is no longer in the list.");
        var streams = _mixer.OpenStreams(key);
        if (streams.Count == 0) return (Unchanged, $"Start playing sound in {entry.Name}, then press Test again.");

        var level = new UserLevel();
        level.Set(entry.Volume, entry.Pan, entry.Muted);
        var processor = new ChainProcessor(level, gateOpen: false);
        processor.SetChain(_chain);
        AudioPipe pipe;
        try
        {
            pipe = await Task.Run(() => AudioPipe.StartAsync(
                ProcessTree.Roots(streams.Select(s => s.ProcessId)).Select(PipeSource.Program).ToList(), new string?[] { streams[0].DeviceId }, processor));
        }
        catch (InvalidOperationException ex)
        {
            return (Unchanged, $"Windows refused to record {entry.Name}: {ex.Message}");
        }

        try
        {
            pipe.TakeCapturePeak();
            await Task.Delay(Timing.CaptureTestListen);
            if (pipe.TakeCapturePeak() < ProgramEngine.Audible)
                return (Unchanged, $"Couldn't hear {entry.Name}. Play something (not paused, not muted) and press Test again.");

            foreach (var method in new[] { SilenceMethod.Mute, SilenceMethod.ZeroVolume })
            {
                if (!await KeepsSoundWhenSilenced(key, entry, method, pipe, processor)) continue;
                processor.GateOpen = true;
                _journal.Note(key, entry, _mixer.DirectRouteOf(key), silenced: true, routed: false);
                _mixer.Silence(key, method);
                await Task.Delay(Timing.CaptureTestPlay);
                var delay = pipe.DelayMs;
                Restore(key);
                var how = method == SilenceMethod.Mute ? "muting the original" : "turning the original's volume to 0";
                return (CaptureTestResult.From(method), $"It works on this PC: the recording kept the sound after {how}. Extra delay about {delay} ms. Effects that ran: {_chain.StageCount}{EngineStatus.SkippedNote(_chain.Skipped)}.");
            }
            return (CaptureTestResult.Failed, $"Muting {entry.Name} also silenced its recording, both ways. Fast mode can't process programs on this PC; use Full control instead.");
        }
        finally
        {
            Restore(key);
            pipe.Dispose();
        }
    }

    /// <summary>Silences the original one way and listens: true when the recording still delivers sound after the mute settled.</summary>
    private async Task<bool> KeepsSoundWhenSilenced(string key, AppAudioEntry entry, SilenceMethod method, AudioPipe pipe, ChainProcessor processor)
    {
        processor.GateOpen = false;
        _journal.Note(key, entry, _mixer.DirectRouteOf(key), silenced: true, routed: false);
        _mixer.Silence(key, method);
        try
        {
            await Task.Delay(Timing.CaptureSilenceSettle);
            pipe.TakeCapturePeak();
            await Task.Delay(Timing.CaptureSilenceProbe - Timing.CaptureSilenceSettle);
            return pipe.TakeCapturePeak() > 0;
        }
        finally
        {
            Restore(key);
        }
    }

    private void Restore(string key)
    {
        _mixer.Unsilence(key);
        _journal.Forget(key);
    }

    /// <summary>A test that could not decide keeps the earlier result.</summary>
    private string Unchanged => _mixer.Settings.CaptureSilence;
}
