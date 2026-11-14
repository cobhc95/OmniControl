using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.Core.Engine;

namespace OmniControl.Tests;

/// <summary>
/// The quiet way of silencing a recorded program (<see cref="SilenceMethod.Quiet"/>): the stored Test results, the check, and the pipe's
/// make-up gain with its ceiling. Live on the owner's PC (own test process, see HANDOVER) the session volume scaled the program's
/// recording linearly (1e-4 = -80.00 dB) and the made-up recording came back at -26.03 dBFS from a -26.02 dBFS tone.
/// </summary>
public class QuietSilenceTests
{
    private static float[] Tone(float peak, int frames = 4800)
    {
        var x = new float[frames * 2];
        for (var i = 0; i < frames; i++) x[2 * i] = x[2 * i + 1] = peak * MathF.Sin(2 * MathF.PI * 1000 * i / 48000);
        return x;
    }

    [Fact]
    public void StoredResults_UntestedAndOldFailures_UseTheQuietWay()
    {
        Assert.Equal(SilenceMethod.Quiet, CaptureTestResult.MethodFor(CaptureTestResult.Untested));
        Assert.Equal(SilenceMethod.Quiet, CaptureTestResult.MethodFor("failed")); // stored before the quiet way existed
        Assert.False(CaptureTestResult.IsTested("failed"));
        Assert.Equal(SilenceMethod.Quiet, CaptureTestResult.MethodFor(CaptureTestResult.Quiet));
        Assert.Equal(SilenceMethod.Mute, CaptureTestResult.MethodFor(CaptureTestResult.Mute));
        Assert.Equal(SilenceMethod.ZeroVolume, CaptureTestResult.MethodFor(CaptureTestResult.ZeroVolume));
        Assert.Null(CaptureTestResult.MethodFor(CaptureTestResult.Failed));
        Assert.Equal(CaptureTestResult.Quiet, CaptureTestResult.From(SilenceMethod.Quiet));
        Assert.Equal(CaptureTestResult.Failed, CaptureTestResult.From(null));
    }

    [Fact]
    public void Check_QuietNeedsSoundThatWasTurnedDown()
    {
        Assert.True(QuietLevel.KeepsSound(SilenceMethod.Quiet, 0.05f * QuietLevel.Scalar)); // a -26 dBFS program held at -80 dB
        Assert.False(QuietLevel.KeepsSound(SilenceMethod.Quiet, 0f)); // the recording went silent
        Assert.False(QuietLevel.KeepsSound(SilenceMethod.Quiet, 0.05f)); // the recording did not follow the volume: never made up
        Assert.True(QuietLevel.KeepsSound(SilenceMethod.Mute, 0.05f));
        Assert.False(QuietLevel.KeepsSound(SilenceMethod.Mute, 0f));
        Assert.Equal(1f, QuietLevel.MakeupFor(SilenceMethod.Mute));
        Assert.Equal(1f / QuietLevel.Scalar, QuietLevel.Makeup);
    }

    [Fact]
    public void Makeup_RestoresTheTurnedDownRecordingExactly()
    {
        var original = Tone(0.05f);
        var recorded = original.Select(s => s * QuietLevel.Scalar).ToArray(); // what the session at 1e-4 delivers
        var mix = new float[original.Length];
        Assert.True(AudioPipe.MixInto(mix, recorded, QuietLevel.Makeup, QuietLevel.RawCeiling));
        var worst = original.Zip(mix, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(worst < 1e-8f, $"largest error {worst}"); // float keeps the -80 dB recording exact (about -150 dB error)
    }

    [Fact]
    public void Makeup_NeverAmplifiesFullLevelSound()
    {
        var mix = new float[9600];
        Assert.False(AudioPipe.MixInto(mix, Tone(0.05f), QuietLevel.Makeup, QuietLevel.RawCeiling)); // session not turned down (yet or any more)
        Assert.All(mix, s => Assert.Equal(0f, s));
        Assert.True(AudioPipe.MixInto(mix, Tone(0.05f), 1f, float.PositiveInfinity)); // plain gain: no ceiling
        Assert.False(AudioPipe.MixInto(mix, Tone(0.05f), 0f, float.PositiveInfinity)); // gain 0 adds nothing
    }
}
