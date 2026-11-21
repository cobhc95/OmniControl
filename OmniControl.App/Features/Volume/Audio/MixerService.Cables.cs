namespace OmniControl.App.Features.Volume.Audio;

/// The user's choices about virtual cables: which found cables OmniControl may use (only the ones switched on) and the device pairs the
/// user marked as cables. Stored here; <see cref="EffectsSupportService"/> applies them to what the PC offers, so every consumer reads the
/// allowed cables from one place.
public sealed partial class MixerService
{
    /// A cable was switched on or off, or a marked pair added or removed (UI thread).
    public event Action? CablesChanged;

    public IReadOnlyList<CableMark> MarkedCables => Settings.MarkedCables;

    public bool IsCableAllowed(string id) => Settings.AllowedCables?.Contains(id) == true;

    public void SetCableAllowed(string id, bool allowed)
    {
        if (IsCableAllowed(id) == allowed) return;
        var list = Settings.AllowedCables ??= new();
        if (allowed) list.Add(id);
        else list.Remove(id);
        CablesSaved();
    }

    /// Marks a playback and a recording device as one cable (a mark with the same recording side is replaced); marking it is the user's
    /// choice to use it, so it is switched on.
    public void MarkCable(string name, string playbackId, string recordingId)
    {
        Settings.MarkedCables.RemoveAll(m => m.RecordingId == recordingId);
        Settings.MarkedCables.Add(new CableMark { Name = name, PlaybackId = playbackId, RecordingId = recordingId });
        var list = Settings.AllowedCables ??= new();
        if (!list.Contains(recordingId)) list.Add(recordingId);
        CablesSaved();
    }

    /// Forgets a marked pair (by its id, its recording side) and its switch.
    public void UnmarkCable(string id)
    {
        if (Settings.MarkedCables.RemoveAll(m => m.RecordingId == id) == 0) return;
        Settings.AllowedCables?.Remove(id);
        CablesSaved();
    }

    /// Sets which cables are switched on (the restore of audio defaults, its undo, a Volume preset); raises <see cref="CablesChanged"/> when that differs.
    public void SetCables(IEnumerable<string> allowed)
    {
        var on = allowed.Distinct().ToList();
        if (Settings.AllowedCables is { } now && on.SequenceEqual(now)) return;
        Settings.AllowedCables = on;
        CablesSaved();
    }

    /// A file from before cables were switched on by hand, with something that may need a cable (<see cref="MixerSettings.Load"/>): every
    /// pair found now that it had not switched off stays on, once, so an existing setup keeps working.
    internal void KeepEarlierCables(IEnumerable<FoundCable> found)
    {
        if (Settings.AllowedCables is not null) return;
        var off = Settings.ForbiddenCables ?? new();
        Settings.AllowedCables = found.Where(f => f.IsPair && !off.Contains(f.Id)).Select(f => f.Id).ToList();
        Settings.ForbiddenCables = null;
        Settings.Save();
    }

    /// Replaces the marked pairs (the restore of audio defaults and its undo); raises <see cref="CablesChanged"/> when that differs.
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
