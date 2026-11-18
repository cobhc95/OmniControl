using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.Tests;

/// <summary>The recorder's kill switch: Abort ends a running recording at once, reports it cancelled and leaves no file. A tiny GIF of the screen's corner is recorded; nothing is shown or changed.</summary>
public class RecordingAbortTests
{
    private static RecordingRequest Request(string path) => new(new ScreenRect(0, 0, 16, 16), path, VideoFormat.Gif, 10, VideoQuality.Low, false, false, false,
        new PointerLook(false, false, 0, 1, 0), new RecordingLimits(TimeSpan.FromMinutes(1), 1 << 24, 0));

    [Fact]
    public async Task AbortEndsTheRecordingAndKeepsNoFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"abort-{Guid.NewGuid():N}.gif");
        var (recorder, _) = await ScreenRecorder.StartAsync(Request(path), _ => { });
        await Task.Delay(200);
        recorder.Abort();
        var result = await recorder.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RecordingEnd.Cancelled, result.End);
        Assert.Null(result.Path);
        await Task.Delay(500); // the thread finishes releasing and deletes the partial file
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task StopStillSavesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stop-{Guid.NewGuid():N}.gif");
        var (recorder, _) = await ScreenRecorder.StartAsync(Request(path), _ => { });
        await Task.Delay(300);
        recorder.Stop();
        var result = await recorder.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RecordingEnd.Stopped, result.End);
        Assert.Equal(path, result.Path);
        Assert.True(File.Exists(path));
        File.Delete(path);
    }
}
