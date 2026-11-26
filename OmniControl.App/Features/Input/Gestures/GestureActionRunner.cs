using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Gestures;

/// Carries out one wheel step (see <see cref="GestureCatalog"/>) with its options: wheel up or wheel down. Runs off the hook thread.
public sealed class GestureActionRunner
{
    private readonly Func<string, bool, int, bool, bool> _runLevel;
    private readonly Action<string> _report;

    /// <param name="runLevel">Moves the OmniControl level with this id up (true) or down by the given percent (or, with the last flag, to the next round stop); false when no action moves it.</param>
    /// <param name="report">Shows a short problem message to the user.</param>
    public GestureActionRunner(Func<string, bool, int, bool, bool> runLevel, Action<string> report)
    {
        _runLevel = runLevel;
        _report = report;
    }

    public void Run(string stepId, OptionValues options, bool wheelUp, MouseEvent at)
    {
        var up = wheelUp != options.Bool(GestureOptions.Reverse);

        if (stepId == GestureCatalog.Opacity)
        {
            var percent = options.Int(GestureOptions.OpacityStep);
            WindowCommands.AdjustOpacity(at.X, at.Y, up ? percent : -percent);
            return;
        }

        if (GestureCatalog.KeysOf(stepId, up) is { } keys)
        {
            if (!KeyChord.TryParse(keys, out var chord)) return;
            for (var i = options.Int(GestureOptions.Steps); i > 0; i--) InputSender.Chord(chord);
            return;
        }

        if (!_runLevel(stepId, up, options.Int(GestureOptions.Amount), options.Bool(GestureOptions.Snap))) _report("That OmniControl action is no longer available, so the gesture did nothing.");
    }
}
