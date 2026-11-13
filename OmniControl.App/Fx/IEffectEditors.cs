using OmniControl.App.Audio;
using OmniControl.Core.Engine;

namespace OmniControl.App.Fx;

/// <summary>
/// Where the effect editors of one FX window show, as its view-model sees it: inside the window (one at a time, the selected
/// effect's: a plug-in's own editor or its settings list; OmniControl's own editors need nothing from here) or popped out into
/// their own windows (any effect). Popped-out windows are remembered while the FX window is open.
/// </summary>
public interface IEffectEditors : IDisposable
{
    /// <summary>
    /// Shows <paramref name="slot"/>'s plug-in editor inside the FX window (null, or a slot that is not a plug-in: none), closing
    /// what was shown before. Returns the editor, or null with the sentence to show instead (popped out, no editor, won't load).
    /// </summary>
    IPluginEditor? Embed(FxSlot? slot, out string notice);

    /// <summary>
    /// Opens <paramref name="slot"/>'s plug-in for its settings list inside the FX window, closing what was shown before. Returns the
    /// list, or null with the sentence to show instead (popped out, won't load, no settings).
    /// </summary>
    PluginParameterList? EmbedParameters(FxSlot slot, out string notice);

    bool IsPoppedOut(FxSlot slot);

    /// <summary>Moves the effect's editor (as it is shown now: editor or settings list) into its own window, above the FX window.</summary>
    void PopOut(FxSlotViewModel effect);

    /// <summary>Closes the effect's own window; its editor shows inside the FX window again.</summary>
    void PutBack(FxSlot slot);

    /// <summary>These effects left the chain: their editors close.</summary>
    void Leaving(IEnumerable<FxSlot> slots);

    /// <summary>A popped-out window opened or closed (also when the user closed it).</summary>
    event Action? Changed;
}
