namespace OmniControl.App.Audio;

/// <summary>Reading mixer.json again while the program runs (an import from another PC wrote it).</summary>
public sealed partial class MixerService
{
    /// <summary>True while <see cref="ReloadFromDisk"/> runs: what follows the mixer's changes (the Volume presets) does not capture the half-way setup.</summary>
    public bool IsReloading { get; private set; }

    /// <summary>
    /// Takes mixer.json from disk as it is now: programs the file knows are added, every program's volume, balance and mute are set (on the
    /// running program too), and the setup the Volume page's restore covers (effect chains, methods, plays on, combined devices, cables) is
    /// brought to the file's through the one step the restore uses, so the engine reacts as to the same change made by hand. What belongs to
    /// this PC alone (the bus cable and real device it keeps, the capture test, the device names) stays as it is here.
    /// </summary>
    public bool ReloadFromDisk()
    {
        var fresh = MixerSettings.Load();
        IsReloading = true;
        try
        {
            foreach (var app in fresh.Apps)
            {
                if (Find(app.Key) is null)
                    Settings.Apps.Add(new AppAudioEntry { Key = app.Key, Name = app.Name, ExePath = app.ExePath, Volume = app.Volume, Pan = app.Pan, Muted = app.Muted, Added = app.Added });
                else
                {
                    SetVolume(app.Key, app.Volume);
                    SetMuted(app.Key, app.Muted);
                    SetPan(app.Key, app.Pan);
                }
            }
            foreach (var input in fresh.Inputs.Where(i => Settings.Inputs.All(have => have.Id != i.Id)))
                Settings.Inputs.Add(new InputFxEntry { Id = input.Id, Name = input.Name, Cable = input.Cable });
            AudioDefaults.Apply(new MixerDefaultsStore(this), fresh);
            SetKeepFxInPlace(fresh.KeepFxInPlace);
            Settings.Save();
            Scan();
        }
        finally
        {
            IsReloading = false;
        }
        return true;
    }
}
