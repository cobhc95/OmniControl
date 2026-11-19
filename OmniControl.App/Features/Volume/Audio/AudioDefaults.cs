using System.Text.Json;

namespace OmniControl.App.Features.Volume.Audio;

/// The part of <see cref="MixerService"/> that restoring the audio defaults (and undoing it) changes, as a small interface so it can
/// be tested with a fake (<see cref="MixerDefaultsStore"/> is the real one: it only forwards, so the engine reacts exactly as it does
/// to the same change made by hand).
public interface IAudioDefaultsMixer
{
    MixerSettings Settings { get; }
    List<FxSlot> FxChain(string? key);
    void SetFxOn(string? key, bool on);
    void SaveFx(string? key);
    void SetFxRoute(string key, EffectsRoute route);
    void SetAdded(string key, bool added);
    void AddCombined(CombinedDevice device);
    void RemoveCombined(string key);
    void SetPlaysOn(string key, IEnumerable<string> targets);
    void SetMasterPlaysOn(IEnumerable<string> targets);
    void SetMasterMethod(EffectsRoute route);
    void SetCables(IEnumerable<string> forbidden);
    void SetMarkedCables(IEnumerable<CableMark> marks);
    void Save();
}

/// Where the copy of the settings taken before a restore is kept (a file next to the other settings), so an undo can read it.
public interface IDefaultsSnapshot
{
    void Save(MixerSettings settings);

    /// The copy, or null when there is none or it cannot be read.
    MixerSettings? Load();
}

/// <see cref="IAudioDefaultsMixer"/> over the real <see cref="MixerService"/>.
public sealed class MixerDefaultsStore : IAudioDefaultsMixer
{
    private readonly MixerService _mixer;

    public MixerDefaultsStore(MixerService mixer) => _mixer = mixer;

    public MixerSettings Settings => _mixer.Settings;
    public List<FxSlot> FxChain(string? key) => _mixer.FxChain(key);
    public void SetFxOn(string? key, bool on) => _mixer.SetFxOn(key, on);
    public void SaveFx(string? key) => _mixer.SaveFx(key);
    public void SetFxRoute(string key, EffectsRoute route) => _mixer.SetFxRoute(key, route);
    public void SetAdded(string key, bool added) => _mixer.SetAdded(key, added);
    public void AddCombined(CombinedDevice device) => _mixer.AddCombined(device);
    public void RemoveCombined(string key) => _mixer.RemoveCombined(key);
    public void SetPlaysOn(string key, IEnumerable<string> targets) => _mixer.SetPlaysOn(key, targets);
    public void SetMasterPlaysOn(IEnumerable<string> targets) => _mixer.SetMasterPlaysOn(targets);
    public void SetMasterMethod(EffectsRoute route) => _mixer.SetMasterMethod(route);
    public void SetCables(IEnumerable<string> forbidden) => _mixer.SetCables(forbidden);
    public void SetMarkedCables(IEnumerable<CableMark> marks) => _mixer.SetMarkedCables(marks);
    public void Save() => _mixer.Save();
}

/// The snapshot as a JSON file in the app data folder (<see cref="AppDataStore"/>).
public sealed class FileDefaultsSnapshot : IDefaultsSnapshot
{
    private static readonly DataStore Store = DataStores.MixerBeforeRestore;

    public void Save(MixerSettings settings) => AppDataStore.Save(Store, settings);

    public MixerSettings? Load() => AppDataStore.Load<MixerSettings>(Store);
}

/// "Restore audio defaults": removes every effect chain (master, programs, microphones), every combined device, every method and route
/// the user set, the hand-marked cables, allows every virtual cable, forgets the "added" mark of
/// programs. Volumes, mutes and balance stay (a program that then holds only defaults is
/// unlisted by the usual listing rules), and so do the user's saved presets (they are not part of this configuration). The state it
/// aims for is worked out from the settings (<see cref="DefaultsOf"/>, pure) and one step (<see cref="Apply"/>) brings the mixer to a
/// given state, which is also how the undo puts the earlier state back, so the two can never disagree about what they touch. Only the
/// user's confirmation starts it.
public static class AudioDefaults
{
    /// These settings, with everything the restore touches at its default.
    public static MixerSettings DefaultsOf(MixerSettings current)
    {
        var target = Copy(current);
        (target.MasterFxOn, target.MasterFxUserOff) = (false, false);
        target.MasterFx = new();
        target.MasterPlaysOn = new();
        target.MasterMethod = EffectsRules.MasterAutomatic.Id;
        target.ForbiddenCables = new();
        target.MarkedCables = new();
        target.Combined = new();
        foreach (var app in target.Apps)
        {
            (app.FxOn, app.FxUserOff) = (false, false);
            app.Fx = new();
            app.FxRoute = EffectsRules.RouteAutomatic.Id;
            app.PlaysOn = new();
            app.Added = false;
        }
        foreach (var input in target.Inputs) (input.FxOn, input.FxUserOff, input.Fx) = (false, false, new());
        return target;
    }

    /// True when the restore would change nothing (the button is greyed then).
    public static bool IsAtDefaults(MixerSettings current) => SameSetup(current, DefaultsOf(current));

    /// True when the two hold the same setup: every setting <see cref="Apply"/> covers (a Volume preset compares the live settings with its own this way).
    public static bool SameSetup(MixerSettings a, MixerSettings b) => Slice(a) == Slice(b);

    /// A deep copy of the settings, to keep as a snapshot (the restore's undo, a Volume preset).
    public static MixerSettings Snapshot(MixerSettings settings) => Copy(settings);

    /// What the restore does, in plain words (the question's text and the page's scope).
    public const string Scope =
        "Removes all effects (master, apps, microphones), all combined devices and every method and route you set, and allows all virtual cables. "
        + "Your volume levels and mutes stay.";

    /// Takes a copy of the settings, then restores the defaults. The copy is made before anything changes.
    public static void Restore(IAudioDefaultsMixer mixer, IDefaultsSnapshot snapshot)
    {
        snapshot.Save(mixer.Settings);
        Apply(mixer, DefaultsOf(mixer.Settings));
    }

    /// Puts back what the restore changed from the copy; false when there is no copy to read.
    public static bool Undo(IAudioDefaultsMixer mixer, IDefaultsSnapshot snapshot)
    {
        if (snapshot.Load() is not { } before) return false;
        Apply(mixer, before);
        return true;
    }

    /// Brings every setting the restore covers to the value in <paramref name="target"/>, touching only what differs. Combined devices come first (removing one clears the "plays on" entries that point at it).
    public static void Apply(IAudioDefaultsMixer mixer, MixerSettings target)
    {
        var now = mixer.Settings;

        foreach (var gone in now.Combined.Where(c => target.Combined.All(t => t.Key != c.Key)).ToList()) mixer.RemoveCombined(gone.Key);
        foreach (var back in target.Combined.Where(t => now.Combined.All(c => c.Key != t.Key)).ToList()) mixer.AddCombined(Copy(back));

        SyncChain(mixer, null, target.MasterFx, target.MasterFxOn, target.MasterFxUserOff);
        foreach (var app in target.Apps)
        {
            if (now.Apps.FirstOrDefault(a => a.Key == app.Key) is not { } have) continue; // forgotten since: nothing to put back
            SyncChain(mixer, app.Key, app.Fx, app.FxOn, app.FxUserOff);
            if (EffectsRules.RouteOf(have.FxRoute) != EffectsRules.RouteOf(app.FxRoute)) mixer.SetFxRoute(app.Key, EffectsRules.RouteOf(app.FxRoute));
            if (!have.PlaysOn.SequenceEqual(app.PlaysOn)) mixer.SetPlaysOn(app.Key, app.PlaysOn);
            if (have.Added != app.Added) mixer.SetAdded(app.Key, app.Added);
        }
        foreach (var input in target.Inputs)
            if (now.Inputs.Any(i => i.Id == input.Id)) SyncChain(mixer, MixerService.InputPrefix + input.Id, input.Fx, input.FxOn, input.FxUserOff);
        foreach (var combined in target.Combined)
            if (now.Combined.Any(c => c.Key == combined.Key)) SyncChain(mixer, combined.Key, combined.Fx, combined.FxOn, combined.FxUserOff);

        mixer.SetMasterPlaysOn(target.MasterPlaysOn);
        mixer.SetMasterMethod(EffectsRules.MasterRouteOf(target.MasterMethod));
        mixer.SetCables(target.ForbiddenCables);
        mixer.SetMarkedCables(target.MarkedCables);
        mixer.Save();
    }

    /// Makes one chain (key null = the master) and its switch what they should be; the engine is told once, by whichever call comes last.
    private static void SyncChain(IAudioDefaultsMixer mixer, string? key, List<FxSlot> chain, bool on, bool userOff)
    {
        SetUserOff(mixer.Settings, key, userOff);
        var have = mixer.FxChain(key);
        var changed = Json(have) != Json(chain);
        if (changed)
        {
            var copy = Copy(chain);
            have.Clear();
            have.AddRange(copy);
        }
        if (IsOn(mixer.Settings, key) != on) mixer.SetFxOn(key, on);
        else if (changed) mixer.SaveFx(key);
    }

    /// Puts back the "the user switched these effects off" flag (data only: it changes nothing the engine runs).
    private static void SetUserOff(MixerSettings s, string? key, bool off)
    {
        if (key is null) s.MasterFxUserOff = off;
        else if (MixerService.IsInput(key)) { if (s.Inputs.FirstOrDefault(i => i.Id == MixerService.InputIdOf(key)) is { } i) i.FxUserOff = off; }
        else if (s.Combined.FirstOrDefault(c => c.Key == key) is { } combined) combined.FxUserOff = off;
        else if (s.Apps.FirstOrDefault(a => a.Key == key) is { } app) app.FxUserOff = off;
    }

    private static bool IsOn(MixerSettings s, string? key) =>
        key is null ? s.MasterFxOn
        : MixerService.IsInput(key) ? s.Inputs.FirstOrDefault(i => i.Id == MixerService.InputIdOf(key))?.FxOn ?? false
        : s.Combined.FirstOrDefault(c => c.Key == key) is { } combined ? combined.FxOn
        : s.Apps.FirstOrDefault(a => a.Key == key)?.FxOn ?? false;

    /// Exactly the settings the restore covers, normalised (an empty id is Automatic), for comparing.
    private static string Slice(MixerSettings s) => Json(new
    {
        s.MasterFxOn,
        s.MasterFxUserOff,
        Chain = Json(s.MasterFx),
        s.MasterPlaysOn,
        Method = EffectsRules.MasterRouteOf(s.MasterMethod).Id,
        s.ForbiddenCables,
        Marked = Json(s.MarkedCables),
        Apps = s.Apps.Select(a => new { a.Key, a.FxOn, a.FxUserOff, Chain = Json(a.Fx), Route = EffectsRules.RouteOf(a.FxRoute).Id, a.PlaysOn, a.Added }),
        Inputs = s.Inputs.Select(i => new { i.Id, i.FxOn, i.FxUserOff, Chain = Json(i.Fx) }),
        Combined = Json(s.Combined),
    });

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    /// A deep copy (the settings are plain data, so serialising them is the copy).
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;
}
