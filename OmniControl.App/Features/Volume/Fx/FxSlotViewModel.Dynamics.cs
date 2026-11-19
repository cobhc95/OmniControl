using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// The Compressor's and the Limiter's editor (design from TabForge's windows): the graph card (<see cref="DynamicsItem"/>), then the
/// settings as cards. The compressor also has TabForge's factory presets in the toolbar's preset box (the shared <see cref="PresetPicker"/>).
public sealed partial class FxSlotViewModel
{
    private DynamicsItem? _dynamics;
    private bool _applyingPreset;

    private IReadOnlyList<object> BuildDynamics()
    {
        var dynamics = _dynamics = new DynamicsItem(_slot, () => _readings(_slot));
        FxCatalog.Complete(_slot); // values saved by older versions get the newer settings' defaults before they are compared with a preset
        if (FxCatalog.KindOf(_slot) is { Presets: { Count: > 0 } } kind)
        {
            _presets = new PresetPicker(new PresetSource(kind, () => new PresetContent { Values = _slot.Values.ToList() }, content => ApplyValues(content.Values!),
                () => _slot.Custom, custom => _slot.Custom = custom), _presetHost, _save);
            _presetChoice = _presets.Choice;
        }
        var cards = BuildParams(() =>
        {
            dynamics.ValuesChanged();
            if (!_applyingPreset) _presets?.Edited();
        });
        return cards.Prepend(dynamics).ToList();
    }

    /// A preset's values go into the slot and the cards (the caller saves once).
    private void ApplyValues(IReadOnlyList<double> values)
    {
        _applyingPreset = true;
        for (var i = 0; i < values.Count && i < _slot.Values.Count; i++)
        {
            _slot.Values[i] = values[i];
            switch (_paramCards[i])
            {
                case SliderItem slider: slider.Load((int)Math.Round(values[i])); break;
                case ToggleItem toggle: toggle.Load(CompressorParams.IsOn(values[i])); break;
            }
        }
        _applyingPreset = false;
        _dynamics?.ValuesChanged();
    }
}
