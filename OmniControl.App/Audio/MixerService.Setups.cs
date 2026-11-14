using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// The numbered setups of the help table (<see cref="SoundModes.HelpCombinations"/>) as settings: choosing one sets the mode, the
/// master's method and, for setup 5, switches off the cables nothing of the user's holds so they stay free for another app (and on
/// again for any other setup); the Full control setup is remembered so switching Fast and Full control back and forth restores it.
/// </summary>
public sealed partial class MixerService
{
    /// <summary>The Full control setup to restore (0 = none chosen yet).</summary>
    public int FullSetup => Settings.FullSetup;

    /// <summary>Setup 5 switched cables off to keep them free.</summary>
    public bool CablesKeptFree => Settings.FreedCables.Count > 0;

    /// <summary>
    /// Applies setup <paramref name="number"/> in one step (<see cref="SoundModes.SetupOf"/>): the mode, in Full control the master's
    /// method (remembered as the Full control setup), and the cables: setup 5 switches off every allowed pair in <paramref name="found"/>
    /// that no combined device or microphone remembers; any other setup switches those back on. The engine plans once, after it all.
    /// </summary>
    public void ApplySetup(int number, IReadOnlyList<FoundCable> found)
    {
        var choice = SoundModes.SetupOf(number);
        var cablesChanged = choice.KeepCablesFree ? KeepCablesFree(found) : FreeCablesAgain();
        Settings.SoundMode = choice.Mode.Id;
        if (choice.Mode == SoundModes.Full)
        {
            Settings.MasterMethod = choice.Master.Id;
            Settings.FullSetup = number;
        }
        if (cablesChanged) CablesChanged?.Invoke();
        Changed();
    }

    /// <summary>Leaving Full control: the setup to restore when it is chosen again (saved with the mode change that follows).</summary>
    public void RememberFullSetup(int number) => Settings.FullSetup = number;

    private bool KeepCablesFree(IReadOnlyList<FoundCable> found)
    {
        var held = Settings.Combined.Select(c => c.Cable).Concat(Settings.Inputs.Select(i => i.Cable)).Where(c => c.Length > 0).ToHashSet();
        var free = found.Where(f => f.IsPair && IsCableAllowed(f.Id) && !f.Inputs.Any(i => held.Contains(i.PlaybackId))).Select(f => f.Id).ToList();
        Settings.ForbiddenCables.AddRange(free);
        Settings.FreedCables.AddRange(free);
        return free.Count > 0;
    }

    private bool FreeCablesAgain()
    {
        if (Settings.FreedCables.Count == 0) return false;
        Settings.ForbiddenCables.RemoveAll(Settings.FreedCables.Contains);
        Settings.FreedCables.Clear();
        return true;
    }
}
