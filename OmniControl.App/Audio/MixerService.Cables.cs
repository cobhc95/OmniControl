using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// The user's choices about virtual cables: which found cables OmniControl may use (all by default) and the device pairs the user
/// marked as cables. Stored here; <see cref="SoundModeService"/> applies them to what the PC offers, so every consumer reads the
/// allowed cables from one place.
/// </summary>
public sealed partial class MixerService
{
    /// <summary>A cable was switched on or off, or a marked pair added or removed (UI thread).</summary>
    public event Action? CablesChanged;

    public IReadOnlySet<string> ForbiddenCables => Settings.ForbiddenCables.ToHashSet();

    public IReadOnlyList<CableMark> MarkedCables => Settings.MarkedCables;

    public bool IsCableAllowed(string id) => !Settings.ForbiddenCables.Contains(id);

    public void SetCableAllowed(string id, bool allowed)
    {
        if (IsCableAllowed(id) == allowed) return;
        if (allowed) Settings.ForbiddenCables.Remove(id);
        else Settings.ForbiddenCables.Add(id);
        Settings.FreedCables.Remove(id); // the user's own choice now, not setup 5's
        CablesSaved();
    }

    /// <summary>Marks a playback and a recording device as one cable (a mark with the same recording side is replaced).</summary>
    public void MarkCable(string name, string playbackId, string recordingId)
    {
        Settings.MarkedCables.RemoveAll(m => m.RecordingId == recordingId);
        Settings.MarkedCables.Add(new CableMark { Name = name, PlaybackId = playbackId, RecordingId = recordingId });
        CablesSaved();
    }

    /// <summary>Forgets a marked pair (by its id, its recording side) and its switch.</summary>
    public void UnmarkCable(string id)
    {
        if (Settings.MarkedCables.RemoveAll(m => m.RecordingId == id) == 0) return;
        Settings.ForbiddenCables.Remove(id);
        CablesSaved();
    }

    private void CablesSaved()
    {
        Settings.Save();
        CablesChanged?.Invoke();
    }
}
