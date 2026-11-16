namespace OmniControl.App.Audio.Engine;

/// <summary>
/// Windows' default playback device while the master bus runs: the bus's cable becomes the default, so every program
/// and system sound goes through the bus, and the real device it replaced is remembered (<see cref="MixerSettings.RealMasterDevice"/>)
/// as where the bus plays and what the master's volume acts on (<see cref="MixerService.MasterDeviceId"/>). When the bus stops running
/// (the master's method set to Equalizer APO, no cable, a failed run, exit) the default goes back to the real device, but only while the cable is still the default:
/// a device the user chose in the meantime is not undone. Never leaves the cable as the default without a running bus. UI thread.
/// </summary>
internal sealed class BusDefault
{
    private readonly MixerService _mixer;
    private readonly Action<string> _report;
    private readonly CableVolumeMirror _mirror;
    /// <summary>The last change Windows refused (default now | cable), so it is not asked again until something differs.</summary>
    private string _refused = string.Empty;
    /// <summary>A default change made here that the mixer's scan has not shown yet (the default before it, the default after it).</summary>
    private (string? From, string To)? _set;
    /// <summary>The device the last "isn't plugged in" sentence was about, so it is said once.</summary>
    private string _fellBackTo = string.Empty;

    public BusDefault(MixerService mixer, Action<string> report)
    {
        _mixer = mixer;
        _report = report;
        _mirror = new CableVolumeMirror(mixer);
    }

    /// <summary>
    /// Brings Windows' default in line with the plan: the bus's cable while its run is up, else the real device. A run that is only
    /// starting again on the same cable (its outputs changed) keeps the cable as the default, so it does not flip back and forth.
    /// </summary>
    public void Sync(BusPlan? bus, bool busExists, bool busRunning)
    {
        switch (StepFor(bus, busExists, busRunning, _mixer.Settings.BusCable))
        {
            case Step.Take:
                Take(bus!);
                break;
            case Step.Give:
                Give();
                break;
        }
    }

    /// <summary>What <see cref="Sync"/> does with Windows' default.</summary>
    internal enum Step { Take, Keep, Give }

    /// <summary>
    /// The rule of <see cref="Sync"/> (pure): the cable becomes the default while the bus runs; a bus only restarting on the cable it owns
    /// keeps it; anything else (no bus planned: the master in Equalizer APO, no cable; a failed bus) gives the default back.
    /// </summary>
    /// <param name="ownedCable">The cable OmniControl made the default (<see cref="MixerSettings.BusCable"/>; empty = none).</param>
    internal static Step StepFor(BusPlan? bus, bool busExists, bool busRunning, string ownedCable) =>
        bus is { Real.Id: not null } && busRunning ? Step.Take
        : bus is not null && busExists && ownedCable == bus.Cable ? Step.Keep
        : Step.Give;

    /// <summary>Giving the default back changes it only while OmniControl's cable is still the default (a device the user chose since is kept).</summary>
    internal static bool PutsBack(string? currentDefault, string ownedCable) => ownedCable.Length > 0 && currentDefault == ownedCable;

    /// <summary>The bus ends for good (exit): Windows' default goes back to the real device.</summary>
    public void Release()
    {
        _mixer.RefreshDevices(); // the answer must be Windows' now, not the last scan's
        _set = null;
        Give();
    }

    private void Take(BusPlan bus)
    {
        var real = bus.Real;
        if (!real.FellBack)
        {
            _fellBackTo = string.Empty;
            if (_mixer.Settings.RealMasterDevice != real.Id)
            {
                _mixer.Settings.RealMasterDevice = real.Id!;
                _mixer.Save();
            }
        }
        else if (_fellBackTo != real.Id)
        {
            _fellBackTo = real.Id!;
            _report($"{_mixer.DeviceNameOf(_mixer.Settings.RealMasterDevice)} isn't plugged in, so the master plays on {_mixer.DeviceNameOf(real.Id!)}.");
        }

        _mixer.SetMasterDevice(real.Id);
        var current = CurrentDefault();
        if (current == bus.Cable)
        {
            Own(bus.Cable);
            _mirror.Start(bus.Cable, real.Id!);
            return;
        }
        var attempt = $"{current}|{bus.Cable}";
        if (attempt == _refused) return;
        Own(bus.Cable); // before the change, so the master's level follows the real device from Windows' first notice
        if (_mixer.TrySetDefaultDevice(bus.Cable, out var error))
        {
            _set = (current, bus.Cable);
            _refused = string.Empty;
            _mirror.Start(bus.Cable, real.Id!);
            return;
        }
        _refused = attempt;
        Disown();
        _mixer.SetMasterDevice(null);
        _report($"Couldn't make {_mixer.DeviceNameOf(bus.Cable)} Windows' default device, so only programs sent to it pass through the master: {error}");
    }

    private void Give()
    {
        _refused = string.Empty;
        _mirror.Stop();
        var cable = _mixer.Settings.BusCable;
        if (cable.Length == 0)
        {
            _mixer.SetMasterDevice(null);
            return;
        }
        var current = CurrentDefault();
        Disown();
        if (PutsBack(current, cable)) PutBack(cable);
        _mixer.SetMasterDevice(null);
    }

    /// <summary>The cable is still the default: back to the remembered real device, or the first real one when that is unplugged.</summary>
    private void PutBack(string cable)
    {
        var real = MasterDevice.Real(_mixer.Devices, null, _mixer.Settings.RealMasterDevice);
        if (real.Id is null)
        {
            _report("No speakers are plugged in to go back to, so Windows' default device is still the virtual cable.");
            return;
        }
        if (real.FellBack) _report($"{_mixer.DeviceNameOf(_mixer.Settings.RealMasterDevice)} isn't plugged in, so Windows' default device is now {_mixer.DeviceNameOf(real.Id)}.");
        if (_mixer.TrySetDefaultDevice(real.Id, out var error)) _set = (cable, real.Id);
        else _report($"Couldn't make {_mixer.DeviceNameOf(real.Id)} Windows' default device again: {error}");
    }

    /// <summary>Windows' default device as far as is known: the last scan's, unless a change made here has not shown in it yet.</summary>
    private string? CurrentDefault()
    {
        var seen = _mixer.DefaultDeviceId;
        if (_set is not { } pending) return seen;
        if (seen == pending.From) return pending.To; // the scan is older than the change
        _set = null; // caught up, or the user chose another device since
        return seen;
    }

    private void Own(string cable)
    {
        if (_mixer.Settings.BusCable == cable) return;
        _mixer.Settings.BusCable = cable;
        _mixer.Save();
    }

    private void Disown() => Own(string.Empty);

    /// <summary>Releases the cable mirror's watchers (the engine is disposed, after <see cref="Release"/>).</summary>
    public void Dispose() => _mirror.Dispose();
}
