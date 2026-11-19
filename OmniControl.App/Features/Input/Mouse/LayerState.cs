using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// The layer in force for each input family: the one chosen (drop-down, next, previous, switch to), unless a "hold for layer" button of
/// that family's layers is down, in which case the last one pressed wins. Used from the hook thread and the UI thread.
internal sealed class LayerState
{
    private readonly object _gate = new();
    private readonly Dictionary<InputFamily, int> _chosen = new() { [InputFamily.Mouse] = 1, [InputFamily.Trackpad] = 1 };
    private readonly List<(MouseButton Button, InputFamily Family, int Layer)> _held = new(); // in the order they were pressed

    public int Current(InputFamily family)
    {
        lock (_gate)
        {
            for (var i = _held.Count - 1; i >= 0; i--)
                if (_held[i].Family == family) return _held[i].Layer;
            return _chosen[family];
        }
    }

    /// Moves the chosen layer one step, or back to layer 1 when <paramref name="direction"/> is 0.
    public void Step(InputFamily family, int direction, bool wrap)
    {
        lock (_gate)
        {
            var next = _chosen[family] + direction;
            _chosen[family] = direction == 0 ? 1
                : wrap ? Shared.Services.Wrap.Index(next - 1, MouseSettings.Layers) + 1 // layers count from 1
                : Math.Clamp(next, 1, MouseSettings.Layers);
        }
    }

    public void Choose(InputFamily family, int layer)
    {
        lock (_gate) _chosen[family] = Math.Clamp(layer, 1, MouseSettings.Layers);
    }

    /// <paramref name="button"/> is down and holds <paramref name="family"/>'s layer <paramref name="layer"/> until it is released.
    public void Hold(MouseButton button, InputFamily family, int layer)
    {
        lock (_gate)
        {
            _held.RemoveAll(h => h.Button == button);
            _held.Add((button, family, Math.Clamp(layer, 1, MouseSettings.Layers)));
        }
    }

    /// Every held layer is let go (a release may have been missed): the families whose layer changed.
    public List<InputFamily> ReleaseAll()
    {
        lock (_gate)
        {
            var families = _held.Select(h => h.Family).Distinct().ToList();
            _held.Clear();
            return families;
        }
    }

    /// <paramref name="button"/> came up: the family whose held layer it let go, or null when it held none.
    public InputFamily? Release(MouseButton button)
    {
        lock (_gate)
        {
            var at = _held.FindIndex(h => h.Button == button);
            if (at < 0) return null;
            var family = _held[at].Family;
            _held.RemoveAt(at);
            return family;
        }
    }
}
