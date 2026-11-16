using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// The user's choices about virtual cables: which found cables OmniControl may use (all by default) and the device pairs the user
/// marked as cables. Stored here; <see cref="EffectsSupportService"/> applies them to what the PC offers, so every consumer reads the
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

    /// <summary>Sets which cables are switched off (the restore of audio defaults, its undo, a Volume preset); raises <see cref="CablesChanged"/> when that differs.</summary>
    public void SetCables(IEnumerable<string> forbidden)
    {
        var off = forbidden.Distinct().ToList();
        if (off.SequenceEqual(Settings.ForbiddenCables)) return;
        Settings.ForbiddenCables = off;
        CablesSaved();
    }

    /// <summary>Replaces the marked pairs (the restore of audio defaults and its undo); raises <see cref="CablesChanged"/> when that differs.</summary>
    public void SetMarkedCables(IEnumerable<CableMark> marks)
    {
        var list = marks.ToList();
        if (System.Text.Json.JsonSerializer.Serialize(list) == System.Text.Json.JsonSerializer.Serialize(Settings.MarkedCables)) return;
        Settings.MarkedCables = list;
        CablesSaved();
    }

    private void CablesSaved()
    {
        Settings.Save();
        CablesChanged?.Invoke();
    }
}
