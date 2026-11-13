using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.App.Display;

/// <summary>
/// Picture changes OmniControl makes in software: brightness, contrast and colour temperature per display (a gamma ramp) and
/// saturation for the whole desktop (the Windows colour effect). Nothing is saved as a setting: every run starts neutral, and
/// everything is put back when the program ends. Because a gamma ramp can outlive the process, the ramp each display had before the
/// first change is also written to <c>software-display.json</c> and put back at the next start-up if the program did not get to do it.
/// Call from the UI thread (the colour effect needs one thread).
/// </summary>
public sealed class SoftwareDisplayService : IDisposable
{
    private const string RecoveryFile = "software-display.json";

    private readonly Dictionary<string, GammaDisplay> _changed = new();
    private readonly ColorEffect _saturation = new();

    public SoftwareDisplayService() => RestoreAfterCrash();

    /// <summary>Applies <paramref name="color"/> to a display; neutral values put its own picture back.</summary>
    public OperationResult Set(string deviceName, SoftwareColor color)
    {
        if (!_changed.TryGetValue(deviceName, out var display))
        {
            if (color.IsNeutral) return OperationResult.Ok(); // nothing was changed, so nothing to apply
            display = new GammaDisplay(deviceName);
            var captured = display.Capture();
            if (!captured.Success) return captured;
            _changed[deviceName] = display;
            SaveRecovery(); // before the first change, so a crash after it can still be undone
        }

        var result = display.Apply(color.Clamped());
        if (color.IsNeutral) Forget(deviceName);
        return result;
    }

    /// <summary>Puts a display's own picture back.</summary>
    public void Reset(string deviceName)
    {
        if (!_changed.TryGetValue(deviceName, out var display)) return;
        display.Restore();
        Forget(deviceName);
    }

    /// <summary>The display is gone (unplugged): drop what was kept for it without touching it.</summary>
    public void Forget(string deviceName)
    {
        if (_changed.Remove(deviceName)) SaveRecovery();
    }

    /// <summary>Sets the desktop-wide saturation (percent; 100 is normal).</summary>
    public OperationResult SetSaturation(int percent) => _saturation.SetSaturation(percent);

    /// <summary>Puts every display and the saturation back as they were.</summary>
    public void ResetAll()
    {
        foreach (var name in _changed.Keys.ToList()) Reset(name);
        _saturation.Reset();
    }

    public void Dispose() => ResetAll();

    private void SaveRecovery()
    {
        var pending = _changed.Where(d => d.Value.Original is not null)
            .ToDictionary(d => d.Key, d => Convert.ToBase64String(ToBytes(d.Value.Original!)));
        if (pending.Count == 0) AppDataStore.Delete(RecoveryFile);
        else AppDataStore.Save(RecoveryFile, pending);
    }

    /// <summary>A previous run ended with displays still changed (it crashed or was killed): write back the ramps it saved.</summary>
    private static void RestoreAfterCrash()
    {
        if (AppDataStore.Load<Dictionary<string, string>>(RecoveryFile) is not { } saved) return;
        foreach (var (device, text) in saved)
        {
            try { GammaDisplay.Write(device, FromBytes(Convert.FromBase64String(text))); }
            catch (FormatException) { /* a damaged entry: leave that display alone */ }
        }
        AppDataStore.Delete(RecoveryFile);
    }

    private static byte[] ToBytes(ushort[] ramp)
    {
        var bytes = new byte[ramp.Length * sizeof(ushort)];
        Buffer.BlockCopy(ramp, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static ushort[] FromBytes(byte[] bytes)
    {
        var ramp = new ushort[bytes.Length / sizeof(ushort)];
        Buffer.BlockCopy(bytes, 0, ramp, 0, ramp.Length * sizeof(ushort));
        return ramp;
    }
}
