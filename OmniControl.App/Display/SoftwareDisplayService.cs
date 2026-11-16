using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.App.Display;

/// <summary>
/// Picture changes OmniControl makes in software: brightness, contrast, gamma, black and white level, channel gains and colour
/// temperature per display (a gamma ramp, <see cref="RampMath"/>), and saturation, grayscale and invert for the whole desktop (the Windows
/// colour effect, <see cref="ColorMatrix"/>). The warm-light schedule is a layer over the temperature: what the user set is kept and
/// the schedule only warms it. Sliders' values are not saved here: every run starts neutral, and everything is put back when the
/// program ends. Because a gamma ramp can outlive the process, the ramp each display had before the first change is also written to
/// <c>software-display.json</c> and put back at the next start-up if the program did not get to do it.
/// Call from the UI thread (the colour effect needs one thread).
/// </summary>
public sealed class SoftwareDisplayService : IDisposable
{
    private static readonly DataStore RecoveryFile = DataStores.SoftwareDisplay;

    private readonly Func<string, IGammaDisplay> _makeDisplay;
    private readonly bool _keepRecovery;
    private readonly Dictionary<string, IGammaDisplay> _changed = new();
    private readonly Dictionary<string, SoftwareColor> _wanted = new();
    private readonly HashSet<string> _refused = new();
    private readonly ColorEffect _effect = new();
    private double _warmFactor;
    private int _warmKelvin = SoftwareColor.TemperatureNeutral;
    private int _saturation = ColorMatrix.SaturationNeutral;
    private bool _invert;

    /// <param name="makeDisplay">Makes the ramp of a display by its device name; Windows' own unless a test gives a fake.</param>
    /// <param name="keepRecovery">False in a test: nothing is read or written under the user's folder.</param>
    public SoftwareDisplayService(Func<string, IGammaDisplay>? makeDisplay = null, bool keepRecovery = true)
    {
        _makeDisplay = makeDisplay ?? (name => new GammaDisplay(name));
        _keepRecovery = keepRecovery;
        if (keepRecovery) RestoreAfterCrash();
    }

    /// <summary>The device names of the displays whose ramp is changed now (for a test and the page's honesty).</summary>
    public IReadOnlyCollection<string> Changed => _changed.Keys;

    /// <summary>A display the service should keep in step with the warm-light schedule even if no slider was moved.</summary>
    public void Track(string deviceName)
    {
        if (!_wanted.ContainsKey(deviceName)) _wanted[deviceName] = SoftwareColor.Neutral;
        if (_warmFactor > 0) Apply(deviceName);
    }

    /// <summary>The display's driver has shown it takes no ramps from programs (or would not give its own): its cards are greyed.</summary>
    public bool IsRefused(string deviceName) => _refused.Contains(deviceName);

    /// <summary>Forgets every refusal so the next change tries again (Refresh).</summary>
    public void ClearRefusals() => _refused.Clear();

    /// <summary>Applies <paramref name="color"/> to a display (with the schedule's warming over its temperature); neutral values put its own picture back.</summary>
    public OperationResult Set(string deviceName, SoftwareColor color)
    {
        _wanted[deviceName] = color.Clamped();
        return Apply(deviceName);
    }

    /// <summary>The schedule wants the picture <paramref name="factor"/> (0-1) of the way towards <paramref name="targetKelvin"/>; every tracked display follows.</summary>
    public void SetWarmth(double factor, int targetKelvin)
    {
        if (factor == _warmFactor && targetKelvin == _warmKelvin) return;
        (_warmFactor, _warmKelvin) = (factor, targetKelvin);
        foreach (var device in _wanted.Keys.ToList()) Apply(device);
    }

    /// <summary>The picture the display should show: what the user set, with the schedule's warming over its temperature.</summary>
    public SoftwareColor Effective(string deviceName)
    {
        var wanted = _wanted.GetValueOrDefault(deviceName, SoftwareColor.Neutral);
        return wanted with { Temperature = WarmSchedule.Kelvin(wanted.Temperature, _warmFactor, _warmKelvin) };
    }

    private OperationResult Apply(string deviceName)
    {
        var color = Effective(deviceName);
        if (_refused.Contains(deviceName)) return OperationResult.Fail(GammaDisplay.DriverRefusal);

        if (!_changed.TryGetValue(deviceName, out var display))
        {
            if (color.IsNeutral) return OperationResult.Ok(); // nothing was changed, so nothing to apply
            display = _makeDisplay(deviceName);
            var captured = display.Capture();
            if (!captured.Success)
            {
                _refused.Add(deviceName);
                return captured;
            }
            _changed[deviceName] = display;
            SaveRecovery(); // before the first change, so a crash after it can still be undone
        }

        var result = display.Apply(color);
        if (display.DriverRefuses) _refused.Add(deviceName);
        if (color.IsNeutral && result.Success) Release(deviceName);
        return result;
    }

    /// <summary>Puts a display's own picture back (the warm-light schedule, if it is active, still warms it).</summary>
    public void Reset(string deviceName) => Set(deviceName, SoftwareColor.Neutral);

    /// <summary>The display is gone (unplugged): drop what was kept for it without touching it.</summary>
    public void Forget(string deviceName)
    {
        _wanted.Remove(deviceName);
        _refused.Remove(deviceName);
        Release(deviceName);
    }

    private void Release(string deviceName)
    {
        if (_changed.Remove(deviceName)) SaveRecovery();
    }

    /// <summary>Sets the whole-desktop colour effect: saturation (percent; 100 is normal; 0 is grayscale) and invert.</summary>
    public OperationResult SetColorEffect(int saturationPercent, bool invert)
    {
        (_saturation, _invert) = (saturationPercent, invert);
        return _effect.Set(saturationPercent, invert);
    }

    /// <summary>Saturation on its own (keeps the invert state).</summary>
    public OperationResult SetSaturation(int percent) => SetColorEffect(percent, _invert);

    /// <summary>Invert on its own (keeps the saturation).</summary>
    public OperationResult SetInvert(bool invert) => SetColorEffect(_saturation, invert);

    /// <summary>Puts every display and the colour effect back as they were (the warm-light schedule, if it is active, still warms the displays).</summary>
    public void ResetAll()
    {
        foreach (var name in _wanted.Keys.ToList()) Reset(name);
        (_saturation, _invert) = (ColorMatrix.SaturationNeutral, false);
        _effect.Reset();
    }

    public void Dispose()
    {
        (_warmFactor, _warmKelvin) = (0, SoftwareColor.TemperatureNeutral);
        ResetAll();
    }

    private void SaveRecovery()
    {
        if (!_keepRecovery) return;
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
