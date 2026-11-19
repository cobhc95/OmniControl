namespace OmniControl.App.Features.Volume.Audio.Ir;

/// Which version of an impulse response file is on disk: its last write time (UTC ticks), 0 when there is no file or it can't be looked at.
/// Both engines key their prepared file on it, so a file replaced under the same name is loaded again.
internal static class IrFileStamp
{
    public static long Of(string path)
    {
        if (path.Length == 0) return 0;
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0; // a path Windows can't look at: treated as a missing file (the sound passes unchanged)
        }
    }
}
