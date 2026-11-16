using OmniControl.App.Dialogs;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Pointer;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// The Pointer settings group's entries in the master action list, made from <see cref="PointerControls"/>: a number gets up/down (a
/// level for the wheel gestures when it names one, with its OSD element), a switch a toggle, the scheme next/previous; plus reset and
/// restore. Also the button-swap trial ("Keep this button swap?") that every swap goes through, from a card, an action or a reset.
/// </summary>
public sealed partial class MousePageViewModel
{
    private void RegisterPointerActions(HotkeyRegistry hotkeys)
    {
        var group = PointerControls.GroupName;
        var actions = new List<HotkeyAction>();
        foreach (var control in PointerControls.All)
        {
            var id = $"pointer.{control.Id}";
            var name = char.ToUpperInvariant(control.ActionName[0]) + control.ActionName[1..];
            switch (control.Kind)
            {
                case PointerKind.Number when control.LevelId is { } levelId:
                    var level = new HotkeyLevel(levelId, control.Label);
                    actions.Add(HotkeyAction.ForLevel($"{id}.up", $"{name} up", Title, control.Keywords, level, +1,
                        (_, percent) => StepPointer(control, control.UnitsOf(percent)), group: group));
                    actions.Add(HotkeyAction.ForLevel($"{id}.down", $"{name} down", Title, control.Keywords, level, -1,
                        (_, percent) => StepPointer(control, control.UnitsOf(percent)), group: group));
                    if (control.Osd is { } element) _osd.Declare(element, OsdLevelIcon.Disc);
                    break;
                case PointerKind.Number:
                    actions.Add(new HotkeyAction($"{id}.up", $"{name} up", Title, control.Keywords, _ => StepPointer(control, control.Step), group: group));
                    actions.Add(new HotkeyAction($"{id}.down", $"{name} down", Title, control.Keywords, _ => StepPointer(control, -control.Step), group: group));
                    break;
                case PointerKind.Switch:
                    actions.Add(new HotkeyAction($"{id}.toggle", $"Toggle {control.ActionName}", Title, control.Keywords, _ => TogglePointer(control), group: group));
                    break;
                case PointerKind.Choice:
                    actions.Add(new HotkeyAction($"{id}.next", $"Next {control.ActionName}", Title, control.Keywords, _ => StepScheme(+1), group: group));
                    actions.Add(new HotkeyAction($"{id}.previous", $"Previous {control.ActionName}", Title, control.Keywords, _ => StepScheme(-1), group: group));
                    break;
            }
        }
        actions.Add(new HotkeyAction("pointer.reset", "Reset pointer settings", Title, PointerKeywords + " reset defaults",
            _ => ResetPointerDefaults(), group: group));
        actions.Add(new HotkeyAction("pointer.restore", "Restore original pointer settings", Title, PointerKeywords + " restore original undo",
            _ => RestorePointerOriginal(), group: group));
        hotkeys.Register(actions);
    }

    /// <summary>An action moves a number by <paramref name="delta"/> of its own units; a card that does not apply now is left alone, with the reason.</summary>
    private void StepPointer(PointerControl control, int delta)
    {
        if (control.Needs is { } needs && _pointer.Read(needs.Value) != (needs.On ? 1 : 0))
        {
            _report(needs.Reason);
            return;
        }
        if (_pointer.Step(control.Value!.Value, delta) is not { } now) return;
        _pointerSliders[control.Value.Value].Load(now);
        if (control.Osd is { } element) _osd.Show(element, Percent.OfRange(now, control.Range.Min, control.Range.Max));
        UpdateRestoreCard();
    }

    /// <summary>An action flips a switch from what Windows has now, through the card (so the swap still asks to be kept).</summary>
    private void TogglePointer(PointerControl control)
    {
        if (_pointer.Read(control.Value!.Value) is not { } now) return;
        var card = _pointerSwitches[control.Value.Value];
        card.Load(now == 1);
        card.IsOn = now != 1;
    }

    private void StepScheme(int direction)
    {
        _schemes = _pointer.Schemes();
        if (_schemes.Count == 0) return;
        var at = _pointer.CurrentScheme(_schemes) is { } current ? _schemes.ToList().IndexOf(current) : -1;
        var next = at < 0 ? (direction > 0 ? 0 : _schemes.Count - 1) : Wrap.Index(at + direction, _schemes.Count);
        ApplyScheme(_schemes[next]);
    }

    /// <summary>
    /// Swaps the buttons (or swaps them back) on trial and asks whether to keep it. The message has Keep focused, so Enter keeps it
    /// even when the clicking button is the unfamiliar one; anything else, or no answer within <see cref="Timing.PointerSwapKeep"/>, puts it back.
    /// </summary>
    private async void AskKeepSwap(bool swapped)
    {
        if (_pointer.ChangeSwap(swapped) is not { } trial)
        {
            LoadSwap();
            return;
        }
        UpdateRestoreCard();
        var seconds = (int)Timing.PointerSwapKeep.TotalSeconds;
        var now = swapped ? "The right button now clicks." : "The left button now clicks.";
        var keep = await _ask(new DialogMessage("Keep this button swap?",
            $"{now} It goes back in {seconds} seconds unless you keep it.", "Keep", Dismiss: trial));
        if (keep) _pointer.KeepSwap();
        else _pointer.PutBackSwap();
    }

    private async void ResetPointerDefaults()
    {
        var ok = await _pointer.ResetToDefaults();
        LoadPointerSettings();
        if (!ok) _report("Windows didn't accept every default, so the cards show what it has now.");
        AskKeepSwap(PointerValues.Info(PointerValue.Swap).Default == 1);
    }

    private async void RestorePointerOriginal()
    {
        if (!_pointer.HasOriginal) return;
        var ok = await _pointer.RestoreOriginal();
        LoadPointerSettings();
        if (!ok) _report("Windows didn't accept every original value, so the cards show what it has now.");
    }
}
