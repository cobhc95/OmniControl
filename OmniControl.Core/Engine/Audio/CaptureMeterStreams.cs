using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OmniControl.Core.Engine;

/// <summary>
/// Windows reports a recording device's peak meter only while something captures from it, so this keeps one quiet shared-mode
/// capture open per device while its meter is shown (the samples are dropped) and closes them when the meters stop.
/// </summary>
public sealed class CaptureMeterStreams : IDisposable
{
    private readonly Dictionary<string, WasapiCapture?> _open = new();
    private readonly Action<string, Exception> _log;

    public CaptureMeterStreams(Action<string, Exception> log) => _log = log;

    /// <summary>Keeps exactly these devices open: opens the new ones, closes the rest. A device that failed is not retried until it leaves the list.</summary>
    public void Keep(IEnumerable<string> ids)
    {
        var want = ids.ToHashSet();
        foreach (var gone in _open.Keys.Where(k => !want.Contains(k)).ToList()) Close(gone);
        foreach (var id in want.Where(i => !_open.ContainsKey(i))) _open[id] = Open(id);
    }

    public void Dispose()
    {
        foreach (var id in _open.Keys.ToList()) Close(id);
    }

    private WasapiCapture? Open(string id)
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            var c = new WasapiCapture(e.GetDevice(id));
            c.StartRecording();
            return c;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            _log("Microphone meter: open the recording device", ex);
            return null;
        }
    }

    private void Close(string id)
    {
        if (_open.Remove(id, out var c) && c is not null)
        {
            try { c.StopRecording(); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { _log("Microphone meter: stop the capture", ex); }
            c.Dispose();
        }
    }
}
