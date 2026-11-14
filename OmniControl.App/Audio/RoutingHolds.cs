namespace OmniControl.App.Audio;

/// <summary>
/// Which device each program is held on by the effects engine. The device is the holder's claim: letting go releases the program only
/// while it is still held on that device. Two runs hand a program over without asking each other: when a program that followed the
/// master gets effects, its own run holds it on its cable first and the master bus lets go of it afterwards, and that late release
/// must not undo the run's hold (it would put the program back on the bus's cable, unprocessed). No Windows calls; UI thread.
/// </summary>
internal sealed class RoutingHolds
{
    private readonly Dictionary<string, string> _held = new();

    public bool IsHeld(string key) => _held.ContainsKey(key);

    /// <summary>The device the program is held on now, or null.</summary>
    public string? DeviceOf(string key) => _held.GetValueOrDefault(key);

    /// <summary>Holds the program on <paramref name="deviceId"/> (the newest holder wins).</summary>
    public void Hold(string key, string deviceId) => _held[key] = deviceId;

    /// <summary>Lets go of the program when it is still held on <paramref name="deviceId"/>; false (nothing changes) when another holder has it.</summary>
    public bool Release(string key, string deviceId) => _held.TryGetValue(key, out var held) && held == deviceId && _held.Remove(key);
}
