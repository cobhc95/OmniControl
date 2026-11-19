using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// Where the effect editors of one FX window show, as its view-model sees it: inside the window (one at a time, the selected
/// effect's: a plug-in's own editor or its settings list; OmniControl's own editors need nothing from here) or popped out into
/// their own windows (any effect). Popped-out windows are remembered while the FX window is open.
public interface IEffectEditors : IDisposable
{
    /// Shows <paramref name="slot"/>'s plug-in editor inside the FX window (null, or a slot that is not a plug-in: none), closing
    /// what was shown before. Returns the editor, or null with the sentence to show instead (popped out, no editor, won't load).
    IPluginEditor? Embed(FxSlot? slot, out string notice);

    /// Opens <paramref name="slot"/>'s plug-in for its settings list inside the FX window, closing what was shown before. Returns the
    /// list, or null with the sentence to show instead (popped out, won't load, no settings).
    PluginParameterList? EmbedParameters(FxSlot slot, out string notice);

    bool IsPoppedOut(FxSlot slot);

    /// Moves the effect's editor (as it is shown now: editor or settings list) into its own window, above the FX window.
    void PopOut(FxSlotViewModel effect);

    /// Closes the effect's own window; its editor shows inside the FX window again.
    void PutBack(FxSlot slot);

    /// These effects left the chain: their editors close.
    void Leaving(IEnumerable<FxSlot> slots);

    /// A popped-out window opened or closed (also when the user closed it).
    event Action? Changed;
}
