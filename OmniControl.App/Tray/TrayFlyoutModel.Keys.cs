using Avalonia.Input;
using OmniControl.App.Services;

namespace OmniControl.App.Tray;

/// <summary>The three parts of the panel the keyboard walks: the slider rows, the tile grid and the foot's buttons (Add, Edit, Open OmniControl).</summary>
public enum TrayPart { Slider, Tile, Foot }

/// <summary>One place in the panel: a part and the position in it (a slider's or tile's index, or the foot button's).</summary>
public readonly record struct TrayFocus(TrayPart Part, int Index);

/// <summary>What the view does after a key was given to <see cref="TrayFlyoutModel.HandleKey"/>.</summary>
public enum TrayKeyResult { Ignored, Handled, OpenMenu }

// The panel's keyboard (opened by its hotkey, or reached by Tab): which key goes where is decided here; the view only reports where the
// focus is now, moves it where the model says (FocusRequested) and opens a menu on request. Enter and Space are left to the focused
// button, which runs its tile or action itself.
public sealed partial class TrayFlyoutModel
{
    /// <summary>How many foot buttons there are (Add, Edit, Open OmniControl, in that order).</summary>
    public const int FootButtons = 3;

    /// <summary>Where the keyboard focus is now, as the view last reported it; null: not on a slider, tile or foot button.</summary>
    public TrayFocus? Current { get; set; }

    /// <summary>How many tiles are in a row of the grid now (read from the layout by the view).</summary>
    public int TileColumns { get; set; } = 1;

    /// <summary>The focus should move here.</summary>
    public event Action<TrayFocus>? FocusRequested;

    /// <summary>The panel's hotkey was pressed: the tray shows the panel, or hides it when it is open.</summary>
    public event Action? ToggleRequested;

    public void RequestToggle() => ToggleRequested?.Invoke();

    /// <summary>The place of a slider or tile object (the focused control's data), or null for anything else.</summary>
    public TrayFocus? Of(object? item) => item switch
    {
        TraySliderItem slider => new TrayFocus(TrayPart.Slider, Sliders.IndexOf(slider)),
        TrayTileItem tile => new TrayFocus(TrayPart.Tile, Tiles.IndexOf(tile)),
        _ => null,
    };

    /// <summary>Puts the focus on the first slider, else the first tile, else the first foot button.</summary>
    public void FocusFirst() => Move(Sliders.Count > 0 ? new(TrayPart.Slider, 0) : Tiles.Count > 0 ? new(TrayPart.Tile, 0) : new(TrayPart.Foot, 0));

    /// <summary>Takes a key pressed while the panel has the keyboard.</summary>
    public TrayKeyResult HandleKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Alt)) return TrayKeyResult.Ignored;
        var menuKey = key == Key.Apps || (key == Key.F10 && modifiers == KeyModifiers.Shift);
        if (key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Tab or Key.Add or Key.Subtract or Key.OemPlus or Key.OemMinus) && !menuKey)
            return TrayKeyResult.Ignored;
        if (Current is not { } current)
        {
            FocusFirst();
            return TrayKeyResult.Handled;
        }
        if (menuKey) return current.Part == TrayPart.Foot ? TrayKeyResult.Ignored : TrayKeyResult.OpenMenu;
        if (key == Key.Tab) return Move(NextPart(current.Part, modifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1));
        if (key is Key.Up or Key.Down) return Move(Vertical(current, Wrap.Direction(key)));
        var step = key is Key.Right or Key.Add or Key.OemPlus ? 1 : -1;
        if (key is Key.Left or Key.Right && current.Part != TrayPart.Slider)
            return Move(current with { Index = Wrap.Index(current.Index + step, current.Part == TrayPart.Tile ? Tiles.Count : FootButtons) });
        if (current.Part == TrayPart.Slider && current.Index < Sliders.Count) Step(Sliders[current.Index], step);
        return TrayKeyResult.Handled;
    }

    private TrayKeyResult Move(TrayFocus to)
    {
        Current = to;
        FocusRequested?.Invoke(to);
        return TrayKeyResult.Handled;
    }

    private static void Step(TraySliderItem slider, int direction)
    {
        if (slider.CanSet) slider.Value = Math.Clamp(slider.Value + direction * TraySliderItem.KeyStep, slider.Minimum, slider.Maximum);
    }

    /// <summary>The first place of the next (or previous) part that has something in it, wrapping round; the foot always has.</summary>
    private TrayFocus NextPart(TrayPart from, int direction)
    {
        var parts = new List<TrayPart>();
        if (Sliders.Count > 0) parts.Add(TrayPart.Slider);
        if (Tiles.Count > 0) parts.Add(TrayPart.Tile);
        parts.Add(TrayPart.Foot);
        return new TrayFocus(parts[Wrap.Index(parts.IndexOf(from) + direction, parts.Count)], 0);
    }

    /// <summary>Up or down in reading order: slider rows, then the tile grid row by row, then round again; from a foot button back into the grid or forward to the top.</summary>
    private TrayFocus Vertical(TrayFocus at, int direction)
    {
        int sliders = Sliders.Count, tiles = Tiles.Count, columns = Math.Max(1, TileColumns);
        var lastSlider = new TrayFocus(TrayPart.Slider, sliders - 1);
        var lastTile = new TrayFocus(TrayPart.Tile, tiles - 1);
        var first = sliders > 0 ? new TrayFocus(TrayPart.Slider, 0) : tiles > 0 ? new TrayFocus(TrayPart.Tile, 0) : at;
        var last = tiles > 0 ? lastTile : sliders > 0 ? lastSlider : at;
        switch (at.Part)
        {
            case TrayPart.Slider:
                var row = at.Index + direction;
                return row >= 0 && row < sliders ? at with { Index = row } : direction > 0 ? (tiles > 0 ? new(TrayPart.Tile, 0) : first) : last;
            case TrayPart.Tile:
                var cell = at.Index + direction * columns;
                if (cell >= 0 && cell < tiles) return at with { Index = cell };
                if (direction < 0) return sliders > 0 ? lastSlider : lastTile;
                return at.Index < (tiles - 1) / columns * columns ? lastTile : first;
            default:
                return direction < 0 ? last : first;
        }
    }
}
