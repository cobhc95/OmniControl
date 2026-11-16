namespace OmniControl.App.Audio;

/// <summary>
/// The part of <see cref="MixerService"/> the Easy setups change, as a small interface so the changes can be tested with a fake and the
/// Easy view-models never reach into the mixer's settings (<see cref="MixerEasyStore"/> is the real one).
/// </summary>
public interface IEasyMixer
{
    List<FxSlot> FxChain(string? key);
    bool IsFxOn(string? key);
    void SetFxOn(string? key, bool on);
    void SaveFx(string? key);
    string InputKey(string deviceId, string name);
    EffectsRoute FxRouteOf(string key);
    void SetFxRoute(string key, EffectsRoute route);
    IReadOnlyList<CombinedDevice> Combined { get; }
    void AddCombined(CombinedDevice device);
    EffectsRoute MasterMethod { get; }
    void SetMasterMethod(EffectsRoute route);
    void SetMasterPlaysOn(IEnumerable<string> targets);
}

/// <summary><see cref="IEasyMixer"/> over the real <see cref="MixerService"/>: it only forwards.</summary>
public sealed class MixerEasyStore : IEasyMixer
{
    private readonly MixerService _mixer;

    public MixerEasyStore(MixerService mixer) => _mixer = mixer;

    public List<FxSlot> FxChain(string? key) => _mixer.FxChain(key);
    public bool IsFxOn(string? key) => _mixer.IsFxOn(key);
    public void SetFxOn(string? key, bool on) => _mixer.SetFxOn(key, on);
    public void SaveFx(string? key) => _mixer.SaveFx(key);
    public string InputKey(string deviceId, string name) => _mixer.InputKey(deviceId, name);
    public EffectsRoute FxRouteOf(string key) => _mixer.FxRouteOf(key);
    public void SetFxRoute(string key, EffectsRoute route) => _mixer.SetFxRoute(key, route);
    public IReadOnlyList<CombinedDevice> Combined => _mixer.Combined;
    public void AddCombined(CombinedDevice device) => _mixer.AddCombined(device);
    public EffectsRoute MasterMethod => _mixer.MasterMethod;
    public void SetMasterMethod(EffectsRoute route) => _mixer.SetMasterMethod(route);
    public void SetMasterPlaysOn(IEnumerable<string> targets) => _mixer.SetMasterPlaysOn(targets);
}

/// <summary>
/// What each Easy setup step changes, one method per step. Every method runs only when the user pressed that step's button, changes
/// exactly what its name says and nothing else, and works through <see cref="IEasyMixer"/>. No UI.
/// </summary>
public static class EasyApply
{
    /// <summary>
    /// Turns the master's chosen sound on (writes its equaliser back and switches the effects on); a flat sound needs nothing and switches
    /// them off when nothing else runs. A shape made in the full effects window is only switched on, never rewritten.
    /// </summary>
    public static void MasterSound(IEasyMixer mixer)
    {
        var chain = mixer.FxChain(null);
        if (EasyEq.Find(chain) is { } slot && EasyEq.IsCustom(slot))
        {
            slot.Enabled = true;
            if (mixer.IsFxOn(null)) mixer.SaveFx(null);
            else mixer.SetFxOn(null, true);
        }
        else Sound(mixer, null, EasyEq.StoredGains(chain));
    }

    /// <summary>
    /// Gives each program its own Parametric equaliser, switched on: flat when it had none (the Sound drawer then shapes it), the one it
    /// has otherwise; and sets how its effects run to Automatic (no cable chosen by hand).
    /// </summary>
    public static void ProgramSounds(IEasyMixer mixer, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            var chain = mixer.FxChain(key);
            if (EasyEq.Find(chain) is { } slot) slot.Enabled = true;
            else EasyEq.Write(chain, EasyEq.Flat());
            if (mixer.FxRouteOf(key) != EffectsRules.RouteAutomatic) mixer.SetFxRoute(key, EffectsRules.RouteAutomatic);
            if (mixer.IsFxOn(key)) mixer.SaveFx(key);
            else mixer.SetFxOn(key, true);
        }
    }

    /// <summary>Puts a look (five gains) on a microphone's chain; effects on, or off for a flat look when nothing else in the chain runs.</summary>
    public static void Microphone(IEasyMixer mixer, string deviceId, string name, IReadOnlyList<int> gains) =>
        Sound(mixer, mixer.InputKey(deviceId, name), gains);

    private static void Sound(IEasyMixer mixer, string? key, IReadOnlyList<int> gains)
    {
        var chain = mixer.FxChain(key);
        EasyEq.Write(chain, gains);
        var on = EasyEq.EffectsOnFor(chain, gains, mixer.IsFxOn(key));
        if (on != mixer.IsFxOn(key)) mixer.SetFxOn(key, on);
        else mixer.SaveFx(key);
    }

    /// <summary>Makes a combined device from these members; returns it.</summary>
    public static CombinedDevice Combine(IEasyMixer mixer, CombinedKind kind, string name, IEnumerable<string> members)
    {
        var device = new CombinedDevice { Name = name.Trim(), Kind = kind, Members = members.Distinct().ToList() };
        mixer.AddCombined(device);
        return device;
    }

    /// <summary>OmniControl's own engine through a cable: a master method set to Equalizer APO goes back to Automatic so the cable bus is what runs.</summary>
    public static void UseOwnEngine(IEasyMixer mixer)
    {
        if (mixer.MasterMethod == EffectsRules.MasterApo) mixer.SetMasterMethod(EffectsRules.MasterAutomatic);
    }

    /// <summary>The master runs in Equalizer APO on the speakers, which frees the cable the bus held.</summary>
    public static void FreeTheCable(IEasyMixer mixer) => mixer.SetMasterMethod(EffectsRules.MasterApo);

    /// <summary>The master plays on this combined device alone.</summary>
    public static void MasterPlaysOn(IEasyMixer mixer, string key) => mixer.SetMasterPlaysOn(new[] { key });

    /// <summary>A combined device with exactly these members (any order) and this kind, or null.</summary>
    public static CombinedDevice? CombinedOf(IReadOnlyList<CombinedDevice> combined, CombinedKind kind, IReadOnlyCollection<string> members) =>
        combined.FirstOrDefault(c => c.Kind == kind && c.Members.Count == members.Count && c.Members.All(members.Contains));

    /// <summary>"<paramref name="first"/>", or the first numbered one ("... 2") that is not taken.</summary>
    public static string FreeName(IEnumerable<string> taken, string first)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = first;
        for (var n = 2; used.Contains(name); n++) name = $"{first} {n}";
        return name;
    }
}
