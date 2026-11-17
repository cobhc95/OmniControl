using OmniControl.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using OmniControl.App.Services;

namespace OmniControl.App.Osd;

/// <summary>
/// The pop-up: topmost, never takes focus, ignores the mouse. Its look comes from <see cref="OsdView"/>.
/// It is shown once at start-up and never hidden: between pop-ups it is parked far off screen with its last frame still drawn, so a
/// pop-up is only a window move (the screen shows the already drawn frame at once) instead of waiting for a first frame.
/// </summary>
public sealed class OsdWindow : Window
{
    // Far off every screen, so the parked window can never sit over (and block the pointer shape of) anything.
    private static readonly PixelPoint Parked = new(-32000, -32000);

    /// <summary>Values drawn per level pop-up at start-up: zero (the muted look), full, then the sample that stays drawn.</summary>
    private static readonly int[] WarmValues = { 0, Percent.Max, OsdView.SampleValue };

    /// <summary>A text pop-up drawn once at start-up, so its code and fonts are ready too; nobody sees it (it is drawn parked).</summary>
    private static readonly OsdMessage WarmMessage = new(OsdIcon.Track, "OmniControl", "OmniControl");

    // What the drawn level visual was built for, and how to change its value in place; null after a text pop-up.
    private (OsdStyle Style, string Background, Color Accent, string Label, OsdLevelIcon Icon)? _built;
    private Action<int>? _setValue;

    // Where the pop-up is on screen (or about to be, while it waits for a new visual's frame); null while parked.
    private PixelPoint? _onScreen;

    public OsdWindow()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        CanResize = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
    }

    /// <summary>
    /// Draws a level pop-up. True when its look changed (a new visual or opacity, whose frame is not drawn yet); false when only the
    /// value changed in place.
    /// </summary>
    public bool Render(OsdSettings settings, string label, OsdLevelIcon icon, int value)
    {
        var look = (settings.Style, settings.Background, settings.ResolveAccent(), label, icon);
        var changed = SetOpacity(settings);
        if (_built != look || _setValue is null)
        {
            Content = OsdView.Build(settings, label, icon, value, out _setValue);
            _built = look;
            changed = true;
        }
        else _setValue(value);
        return changed;
    }

    public void RenderMessage(OsdSettings settings, OsdMessage message)
    {
        Content = OsdView.BuildMessage(settings, message);
        (_built, _setValue) = (null, null);
        SetOpacity(settings);
    }

    /// <summary>True when the opacity changed.</summary>
    private bool SetOpacity(OsdSettings settings)
    {
        var opacity = OsdSettings.ClampOpacity(settings.Opacity) / (double)Percent.Max;
        if (opacity == Opacity) return false;
        Opacity = opacity;
        return true;
    }

    /// <summary>
    /// Shows the window parked and draws, one real frame each, a text pop-up and then every level pop-up of
    /// <paramref name="elements"/> at several values, so fonts, shapes, the value-changing code, the render thread and the graphics
    /// device have all run before the first real pop-up (the first run of any of them is slow). Ends with the first label drawn
    /// (the likeliest next one).
    /// </summary>
    public void Warm(OsdSettings settings, IReadOnlyList<OsdElement> elements)
    {
        Position = Parked;
        Show();
        Position = Parked; // some platforms reset it on first show
        ClickThrough.Apply(this);

        var steps = new List<Action> { () => RenderMessage(settings, WarmMessage) };
        for (var i = elements.Count - 1; i >= 0; i--)
        {
            if (elements[i] is not { Icon: { } icon } element) continue; // a text pop-up has no level to draw
            foreach (var value in WarmValues) steps.Add(() => Render(settings, element.Name, icon, value));
        }
        WarmStep(steps, 0);
    }

    private void WarmStep(IReadOnlyList<Action> steps, int index)
    {
        if (index >= steps.Count || _onScreen is not null) return; // a real pop-up took over: leave its content alone
        steps[index]();
        RequestAnimationFrame(_ => WarmStep(steps, index + 1));
    }

    /// <summary>Draws the parked pop-up again off screen, so its drawing pipeline and memory stay in use while nothing is shown.</summary>
    public void KeepWarm()
    {
        if (_onScreen is null) Redraw();
    }

    /// <summary>Parks the pop-up off screen. Its frame stays drawn, ready for the next pop-up.</summary>
    public void Fade()
    {
        _onScreen = null;
        Position = Parked;
    }

    /// <summary>
    /// Moves the pop-up to <paramref name="spot"/> on <paramref name="screen"/> without stealing focus. With
    /// <paramref name="newVisual"/> the move waits until the new visual's frame is drawn, so the previous pop-up's frame never flashes.
    /// When the pop-up is already there (values streaming in), nothing moves: the new value draws in place.
    /// </summary>
    public void ShowAt(OsdSpot spot, Screen screen, bool newVisual)
    {
        if (Place(spot, screen) is not { } target || _onScreen == target) return;
        _onScreen = target;
        if (!newVisual) MoveOnScreen();
        else
        {
            // The callback runs before the next frame; one frame later the new visual has been drawn.
            RequestAnimationFrame(_ => RequestAnimationFrame(_ => MoveOnScreen()));
        }
    }

    private void MoveOnScreen()
    {
        if (_onScreen is not { } target || Position == target) return; // faded meanwhile, or already moved by an earlier request
        Position = target;
        ClickThrough.Apply(this); // on every move: Windows can drop the click-through style when the window changes
        // A value changed while the window was parked may not have been drawn (nothing on screen needs the frame), and it would then
        // wait for the next keep-warm redraw. Ask for it now, and once more after the move.
        Redraw();
        RequestAnimationFrame(_ => Redraw());
    }

    private void Redraw() => (Content as Control)?.InvalidateVisual();

    /// <summary>Where the pop-up goes on <paramref name="screen"/> for its current content, in screen pixels.</summary>
    private PixelPoint? Place(OsdSpot spot, Screen screen)
    {
        if (Content is not Control content) return null;
        return OsdPlacement.Position(spot, screen.WorkingArea, screen.Scaling, OsdPlacement.SizeOf(content, screen.Scaling));
    }
}
