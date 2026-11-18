using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.Tests;

/// <summary>
/// The recorder's chain of ways and its watchdog, for real on this PC: a small corner of the screen is recorded into the temp folder while
/// test faults make a way fail, hang (when opening, on the first frame, when finishing) or encode nothing, and the next way must take over,
/// the result must say what to remember, and the log must say why. Also the 4K writer, the frame-rate cap, the forced adapter and the
/// encoder facts. Nothing on the PC is changed; the files are deleted and every hung test thread is let go.
/// </summary>
[Collection(nameof(RecordingFileTests))] // one recording at a time: they share the screen copy and the encoder
public class RecordingChainTests
{
    private static readonly RecordingWaits Short = new(OpenMs: 4000, FirstFrameMs: 3000, StuckMs: 2500, FinishMs: 3000);

    private static RecordingRequest Request(string path, bool hardware = true, int frameRate = 30, RecordingRoute? route = null, VideoFormat format = VideoFormat.Mp4H264) =>
        new(new ScreenRect(0, 0, 128, 96), path, format, frameRate, VideoQuality.Low, hardware, false, false,
            new PointerLook(false, false, 0, 1, 0), new RecordingLimits(TimeSpan.FromMinutes(1), 1L << 30, 0), route);

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"rec-chain-{Guid.NewGuid():N}.mp4");

    private sealed record Run(RecordingResult Result, List<string> Log, List<string> Notices)
    {
        public string Text => string.Join(Environment.NewLine, Log);
    }

    private static async Task<Run> Record(RecordingRequest request, RecordingFaults? faults = null, double seconds = 2.0)
    {
        var log = new List<string>();
        var notices = new List<string>();
        try
        {
            var (recorder, _) = await ScreenRecorder.StartAsync(request, line => { lock (log) log.Add(line); }, n => { lock (notices) notices.Add(n); }, Short, faults);
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            recorder.Stop();
            var result = await recorder.Completion.WaitAsync(TimeSpan.FromSeconds(20));
            return new Run(result, log, notices);
        }
        finally
        {
            faults?.Release.Set(); // hung test threads go on, release what they hold and delete their part files
        }
    }

    private static void AssertPlayable(RecordingResult result, string log)
    {
        Assert.True(result.End == RecordingEnd.Stopped, $"{result.End}: {result.Message}\n{log}");
        Assert.NotNull(result.Path);
        var (_, frames) = Mp4Probe.Read(result.Path!);
        Assert.True(frames >= 10, $"only {frames} frames\n{log}");
    }

    private static void Cleanup(string path)
    {
        Thread.Sleep(300);
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*")) File.Delete(file);
    }

    private static string MachineKey() => GraphicsAdapters.MachineKey(GraphicsAdapters.List(new RecordingLog(_ => { })));

    [Fact]
    public async Task TheGraphicsCardWayRecordsAndNothingIsRemembered()
    {
        var path = NewPath();
        try
        {
            var run = await Record(Request(path));
            AssertPlayable(run.Result, run.Text);
            Assert.Null(run.Result.Route);
            Assert.Contains(run.Log, l => l.Contains("LUID"));
            Assert.Contains(run.Log, l => l.Contains("first frame encoded"));
            Assert.Contains(run.Log, l => l.Contains("Finalize") && l.Contains("done in"));
            Assert.False(File.Exists(path + ".part1"));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Theory]
    [InlineData((int)RecordingFault.Fail)]
    [InlineData((int)RecordingFault.HangOpen)]
    [InlineData((int)RecordingFault.HangWrite)]
    [InlineData((int)RecordingFault.Silent)]
    public async Task AGraphicsCardFaultMovesOnToTheNextWayAndIsRemembered(int faultNumber)
    {
        var fault = (RecordingFault)faultNumber;
        var path = NewPath();
        var faults = new RecordingFaults();
        faults.At[RecordingStep.GraphicsCard] = fault;
        try
        {
            var run = await Record(Request(path), faults, fault is RecordingFault.HangWrite or RecordingFault.Silent ? 6 : 2);
            AssertPlayable(run.Result, run.Text);
            Assert.Equal(new RecordingRoute(MachineKey(), RecordingStep.OtherEncoder), run.Result.Route);
            Assert.Contains(run.Log, l => l.Contains("GraphicsCard given up"));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task BothHardwareWaysFailingEndsInSoftware()
    {
        var path = NewPath();
        var faults = new RecordingFaults();
        faults.At[RecordingStep.GraphicsCard] = RecordingFault.Fail;
        faults.At[RecordingStep.OtherEncoder] = RecordingFault.Fail;
        try
        {
            var run = await Record(Request(path), faults);
            AssertPlayable(run.Result, run.Text);
            Assert.Equal(RecordingStep.Software, run.Result.Route?.Step);
            Assert.Contains(run.Log, l => l.Contains("encoder") && l.Contains("software"));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task NoWayWorkingFailsInOneSentenceWithDetails()
    {
        var faults = new RecordingFaults();
        foreach (var step in new[] { RecordingStep.GraphicsCard, RecordingStep.OtherEncoder, RecordingStep.Software }) faults.At[step] = RecordingFault.Fail;
        var path = NewPath();
        try
        {
            var ex = await Assert.ThrowsAsync<RecordingFailedException>(() => Record(Request(path), faults));
            Assert.Contains("no way of recording worked", ex.Message);
            Assert.Contains("Software failed", ex.Details);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task AFinishThatHangsEndsAsFailedAndTheNextWayIsRemembered()
    {
        var path = NewPath();
        var faults = new RecordingFaults();
        faults.At[RecordingStep.GraphicsCard] = RecordingFault.HangFinish;
        try
        {
            var run = await Record(Request(path), faults);
            Assert.Equal(RecordingEnd.Failed, run.Result.End);
            Assert.Null(run.Result.Path);
            Assert.Equal(new RecordingRoute(MachineKey(), RecordingStep.OtherEncoder), run.Result.Route);
            Assert.False(string.IsNullOrEmpty(run.Result.Details));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task ARememberedWayIsUsedOnTheSameDriversOnly()
    {
        var path = NewPath();
        try
        {
            var same = await Record(Request(path, route: new RecordingRoute(MachineKey(), RecordingStep.Software)));
            AssertPlayable(same.Result, same.Text);
            Assert.DoesNotContain(same.Log, l => l.Contains("trying GraphicsCard"));
            Assert.Equal(RecordingStep.Software, same.Result.Route?.Step);
            Cleanup(path);

            var changed = await Record(Request(path, route: new RecordingRoute("an older driver", RecordingStep.Software)));
            AssertPlayable(changed.Result, changed.Text);
            Assert.Contains(changed.Log, l => l.Contains("trying GraphicsCard"));
            Assert.Null(changed.Result.Route);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task TheOtherEncoderWayTakesAForcedAdapter()
    {
        var log = new RecordingLog(_ => { });
        var owner = GraphicsAdapters.List(log).First(a => a.Shows(new ScreenRect(0, 0, 128, 96)));
        var path = NewPath();
        var faults = new RecordingFaults { Adapter = owner.Luid };
        faults.At[RecordingStep.GraphicsCard] = RecordingFault.Fail;
        try
        {
            var run = await Record(Request(path), faults);
            AssertPlayable(run.Result, run.Text);
            Assert.Contains(run.Log, l => l.Contains($"Direct3D device on adapter LUID {owner.LuidText}") && l.Contains("done in"));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task A140HzFrameRateIsCappedAt60()
    {
        Assert.Equal(VideoMath.Mp4MaxFrameRate, VideoMath.FrameRateFor(VideoFormat.Mp4H264, 140));
        var path = NewPath();
        try
        {
            var run = await Record(Request(path, hardware: false, frameRate: 140), seconds: 2.5);
            AssertPlayable(run.Result, run.Text);
            Assert.Contains(run.Log, l => l.Contains("140 fps asked, 60 used"));
            var (_, frames) = Mp4Probe.Read(run.Result.Path!);
            Assert.True(frames <= 2.5 * 60 + 15, $"{frames} frames for 2.5 s");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A4KWriterFinishesAPlayableFileAndSaysItsEncoder(bool hardware)
    {
        var path = NewPath();
        const int width = 3840, height = 2160, rate = 60, count = 30;
        var lines = new List<string>();
        var frame = new byte[width * height * 4];
        var ticks = VideoMath.FrameTicks(rate);
        try
        {
            using (var writer = Mp4Writer.Open(path, width, height, rate, VideoFormat.Mp4H264,
                       VideoMath.Bitrate(width, height, rate, VideoQuality.High, VideoFormat.Mp4H264), hardware, new RecordingLog(lines.Add)))
            {
                for (var i = 0; i < count; i++)
                {
                    Array.Fill(frame, (byte)(i * 8));
                    writer.WriteFrame(frame, width * 4, i * ticks);
                }
                writer.Finish(count * ticks);
                var encoder = writer.Encoder;
                Assert.NotNull(encoder);
                Assert.Contains("3840x2160", encoder!.Input + encoder.Output);
                if (encoder.Hardware) Assert.True(encoder.Async, "a hardware encoder that is not async: " + encoder); // Windows requires hardware encoders to be async; the Sink Writer drives them
            }
            var (_, frames) = Mp4Probe.Read(path);
            Assert.Equal(count, frames);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
