using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// Copied effects, shared by every FX window (Ctrl+C in one chain, Ctrl+V in another). Kept inside OmniControl, not on the Windows
/// clipboard. A plug-in is copied with its settings as they are now (<paramref name="saveStates"/> stores them first).
public sealed class FxClipboard
{
    private readonly Action _saveStates;
    private List<FxSlot> _copied = new();

    /// <param name="saveStates">Copies every loaded plug-in's current settings into its slot.</param>
    public FxClipboard(Action saveStates) => _saveStates = saveStates;

    public bool HasEffects => _copied.Count > 0;

    public void Copy(IEnumerable<FxSlot> slots)
    {
        _copied = Snapshot(slots);
    }

    /// Independent copies with every plug-in's current settings stored first (what a saved chain file holds too).
    public List<FxSlot> Snapshot(IEnumerable<FxSlot> slots)
    {
        _saveStates();
        return slots.Select(s => s.Copy()).ToList();
    }

    /// New copies of what was copied (pasting twice gives two independent sets).
    public List<FxSlot> Paste() => _copied.Select(s => s.Copy()).ToList();
}
