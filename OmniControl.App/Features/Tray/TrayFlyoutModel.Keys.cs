using Avalonia.Input;

namespace OmniControl.App.Features.Tray;

/// The three parts of the panel the keyboard walks, top to bottom: the tile grid, the slider rows and the foot's buttons.
public enum TrayPart { Tile, Slider, Foot }

/// One place in the panel: a part and the position in it (a slider's or tile's index, or the foot button's).
public readonly record struct TrayFocus(TrayPart Part, int Index);

/// What the view does after a key was given to <see cref="TrayFlyoutModel.HandleKey"/>.
public enum TrayKeyResult { Ignored, Handled, OpenMenu }

// The panel's keyboard (opened by its hotkey, or reached by Tab): which key goes where is decided here; the view only reports where the
// focus is now, moves it where the model says (FocusRequested) and opens a menu on request. Enter and Space are left to the focused
// button, which runs its tile or action itself.
public sealed partial class TrayFlyoutModel
{
    /// How many foot buttons there are (+ Tile, + Slider, Edit, Open OmniControl, in that order).
    public const int FootButtons = 4;

    /// Where the keyboard focus is now, as the view last reported it; null: not on a slider, tile or foot button.
    public TrayFocus? Current { get; set; }

    /// How many tiles are in a row of the grid now (read from the layout by the view).
    public int TileColumns { get; set; } = 1;

    /// The focus should move here.
    public event Action<TrayFocus>? FocusRequested;

    /// The panel's hotkey was pressed: the tray shows the panel, or hides it when it is open.
    /// The master action that shows or hides the panel (the tray icon's single click, the gadget's click and the hotkey all end in <see cref="RequestToggle"/>).
    public const string ToggleActionId = "app.tray.toggle";

    public event Action? ToggleRequested;

    public void RequestToggle() => ToggleRequested?.Invoke();

    /// The place of a slider or tile object (the focused control's data), or null for anything else.
    public TrayFocus? Of(object? item) => item switch
    {
        TraySliderItem slider => new TrayFocus(TrayPart.Slider, Sliders.IndexOf(slider)),
        TrayTileItem tile => new TrayFocus(TrayPart.Tile, Tiles.IndexOf(tile)),
        _ => null,
    };

    /// Puts the focus on the first tile, else the first slider, else the first foot button.
    public void FocusFirst() => Move(Tiles.Count > 0 ? new(TrayPart.Tile, 0) : Sliders.Count > 0 ? new(TrayPart.Slider, 0) : new(TrayPart.Foot, 0));

    /// Takes a key pressed while the panel has the keyboard.
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

    /// The first place of the next (or previous) part that has something in it, wrapping round; the foot always has.
    private TrayFocus NextPart(TrayPart from, int direction)
    {
        var parts = new List<TrayPart>();
        if (Tiles.Count > 0) parts.Add(TrayPart.Tile);
        if (Sliders.Count > 0) parts.Add(TrayPart.Slider);
        parts.Add(TrayPart.Foot);
        return new TrayFocus(parts[Wrap.Index(parts.IndexOf(from) + direction, parts.Count)], 0);
    }

    /// Up or down in reading order: the tile grid row by row, then the slider rows, then round again; from a foot button back to the last row or forward to the top.
    private TrayFocus Vertical(TrayFocus at, int direction)
    {
        int sliders = Sliders.Count, tiles = Tiles.Count, columns = Math.Max(1, TileColumns);
        var lastSlider = new TrayFocus(TrayPart.Slider, sliders - 1);
        var lastTile = new TrayFocus(TrayPart.Tile, tiles - 1);
        var first = tiles > 0 ? new TrayFocus(TrayPart.Tile, 0) : sliders > 0 ? new TrayFocus(TrayPart.Slider, 0) : at;
        var last = sliders > 0 ? lastSlider : tiles > 0 ? lastTile : at;
        switch (at.Part)
        {
            case TrayPart.Tile:
                var cell = at.Index + direction * columns;
                if (cell >= 0 && cell < tiles) return at with { Index = cell };
                if (direction < 0) return last;
                if (at.Index < (tiles - 1) / columns * columns) return lastTile;
                return sliders > 0 ? new(TrayPart.Slider, 0) : first;
            case TrayPart.Slider:
                var row = at.Index + direction;
                return row >= 0 && row < sliders ? at with { Index = row } : direction > 0 ? first : (tiles > 0 ? lastTile : last);
            default:
                return direction < 0 ? last : first;
        }
    }
}
