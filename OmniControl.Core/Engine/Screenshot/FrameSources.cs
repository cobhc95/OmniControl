namespace OmniControl.Core.Engine.Screenshot;

/// <summary>Copies the area once and hands the frame to the file; one per way of recording, made by <see cref="RecordingAttempt"/>.</summary>
internal interface IFrameSource : IDisposable
{
    /// <summary>Takes the frame shown from <paramref name="at"/> on and writes it. False when it was skipped (the encoder still holds every pooled frame).</summary>
    bool Write(long at);
}

/// <summary>Frames copied with GDI into memory (<see cref="FrameGrabber"/>) and written by any sink: the GIF, and the MP4 off the graphics-card path.</summary>
internal sealed class GdiFrames(FrameGrabber grabber, IVideoSink sink) : IFrameSource
{
    public bool Write(long at)
    {
        sink.WriteFrame(grabber.Grab(), grabber.Stride, at);
        return true;
    }

    public void Dispose() => grabber.Dispose();
}

/// <summary>Frames that stay on the graphics card: copied by Desktop Duplication, converted and encoded there by the MP4 writer.</summary>
internal sealed class GpuFrames(DesktopDuplication capture, Mp4Writer writer) : IFrameSource
{
    public bool Write(long at)
    {
        capture.Grab();
        return writer.WriteFrame(capture, at); // false = the encoder still holds every pooled frame: this one is skipped, the video stays in time
    }

    public void Dispose() => capture.Dispose();
}
