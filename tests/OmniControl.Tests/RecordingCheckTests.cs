using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using static OmniControl.App.Audio.Engine.RecordingCheck;

namespace OmniControl.Tests;

/// <summary>
/// The check a recorded program's run makes once it is armed (original silenced, replay open), on a made-up clock with the engine's
/// 100 ms tick: what the row shows, and when it gives up and puts the original back.
/// </summary>
public class RecordingCheckTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Probe = TimeSpan.FromMilliseconds(1500), Idle = TimeSpan.FromSeconds(2);
    private static readonly DateTime T0 = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A program at -26 dBFS as the recording sees it with its session held at the quiet level (-80 dB).</summary>
    private const float QuietRecording = 0.05f * QuietLevel.Scalar;
    private const float ProgramMeter = 0.05f;

    private static RecordingCheck New(SilenceMethod method = SilenceMethod.Quiet) => new(method, Probe, Idle);

    /// <summary>
    /// The start-up budget: an armed run needs no look at all before sound passes (the replay is open from the moment the pipe is up),
    /// and the very first look that sees sound says Running; no step waits for a tick, a scan or a probe time.
    /// </summary>
    [Fact]
    public void Armed_FirstSound_IsRunningAtTheFirstLook()
    {
        var check = New();
        Assert.Equal(Verdict.Ready, check.Look(T0, 0, 0));
        Assert.Equal(Verdict.Ready, check.Look(T0 + Tick, 0, 0));
        Assert.Equal(Verdict.Running, check.Look(T0 + 2 * Tick, QuietRecording, ProgramMeter));
        Assert.True(check.Proved);
    }

    [Fact]
    public void Paused_SaysReadyAfterTheIdleTime_AndRunningAgainAtOnce()
    {
        var check = New();
        check.Look(T0, QuietRecording, ProgramMeter);
        Assert.Equal(Verdict.Running, check.Look(T0 + Idle - Tick, 0, 0)); // a short gap between songs keeps "running"
        Assert.Equal(Verdict.Ready, check.Look(T0 + Idle, 0, 0));
        Assert.Equal(Verdict.Running, check.Look(T0 + Idle + Tick, QuietRecording, ProgramMeter));
    }

    /// <summary>The program plays (Windows' meter) but the recording stays silent: checking, then failed after the probe time.</summary>
    [Fact]
    public void RecordingSilent_WhileTheProgramPlays_FailsAfterTheProbe()
    {
        var check = New(SilenceMethod.Mute);
        var t = T0;
        Assert.Equal(Verdict.Checking, check.Look(t, 0, ProgramMeter));
        for (t += Tick; t - T0 < Probe; t += Tick) Assert.Equal(Verdict.Checking, check.Look(t, 0, ProgramMeter));
        Assert.Equal(Verdict.Failed, check.Look(t, 0, ProgramMeter));
        Assert.Contains("went silent", check.FailReason);
    }

    /// <summary>A recording that stays loud is taken before the session's volume: never made up, and the reason says so.</summary>
    [Fact]
    public void RecordingStaysLoud_FailsWithTheVolumeReason()
    {
        var check = New();
        Assert.Equal(Verdict.Checking, check.Look(T0, ProgramMeter, ProgramMeter));
        Assert.Equal(Verdict.Failed, check.Look(T0 + Probe, ProgramMeter, ProgramMeter));
        Assert.Contains("did not follow", check.FailReason);
    }

    /// <summary>Armed while it already played: loud blocks recorded before the silencing reached the stream, then passing sound.</summary>
    [Fact]
    public void LoudBlocksBeforeTheSilencing_ThenQuiet_Runs()
    {
        var check = New();
        Assert.Equal(Verdict.Checking, check.Look(T0, ProgramMeter, ProgramMeter));
        Assert.Equal(Verdict.Running, check.Look(T0 + Tick, QuietRecording, ProgramMeter));
    }

    /// <summary>Silence everywhere never fails (a paused or silent program is not a failed recording).</summary>
    [Fact]
    public void SilentProgram_StaysReadyForever()
    {
        var check = New();
        for (var t = T0; t - T0 < TimeSpan.FromMinutes(1); t += Tick) Assert.Equal(Verdict.Ready, check.Look(t, 0, 0));
    }

    /// <summary>With the mute or volume-0 way the recording is not made up: full-level sound passes.</summary>
    [Theory]
    [InlineData(SilenceMethod.Mute)]
    [InlineData(SilenceMethod.ZeroVolume)]
    public void MuteAndZeroVolume_FullLevelRecordingPasses(SilenceMethod method) =>
        Assert.Equal(Verdict.Running, New(method).Look(T0, ProgramMeter, ProgramMeter));
}
