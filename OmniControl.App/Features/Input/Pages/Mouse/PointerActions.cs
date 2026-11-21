using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.Osd;
using OmniControl.Core.Engine.Pointer;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// The Pointer settings group's entries in the master action list, made from <see cref="PointerControls"/>: a number gets up/down (a
/// level for the wheel gestures when it names one, with its OSD element), a switch a toggle, the scheme next/previous; plus reset and
/// restore. Also the steps the page's cards share with them: the button-swap trial ("Keep this button swap?") that every swap goes through
/// (a card, an action or a reset), a scheme, reset and restore. Owned by the Input module, so the actions work before the Mouse and
/// Trackpad page is made; the page shows what Windows has again on <see cref="Changed"/>.
public sealed class PointerActions : IDisposable
{
    public const string Keywords = "pointer cursor mouse settings";

    private readonly PointerSettingsService _pointer;
    private readonly IOsd _osd;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action<string> _report;

    /// <param name="ask">Shows a short message with a confirm button (the button-swap "Keep" question).</param>
    public PointerActions(PointerSettingsService pointer, IOsd osd, Func<DialogMessage, Task<bool>> ask, Action<string> report)
    {
        (_pointer, _osd, _ask, _report) = (pointer, osd, ask, report);
        _pointer.WriteFailed += OnWriteFailed;
    }

    /// Something here changed Windows' pointer settings, or Windows refused a write: the page's cards show what Windows has now.
    public event Action? Changed;

    public void Register(IActionRegistry hotkeys, string title)
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
                    actions.Add(HotkeyAction.ForLevel($"{id}.up", $"{name} up", title, control.Keywords, level, +1,
                        (_, percent) => Step(control, control.UnitsOf(percent)), group: group));
                    actions.Add(HotkeyAction.ForLevel($"{id}.down", $"{name} down", title, control.Keywords, level, -1,
                        (_, percent) => Step(control, control.UnitsOf(percent)), group: group));
                    if (control.Osd is { } element) _osd.Declare(element, OsdLevelIcon.Disc);
                    break;
                case PointerKind.Number:
                    var numberFamily = new ActionFamily(id, control.Label);
                    actions.Add(new HotkeyAction($"{id}.up", $"{name} up", title, control.Keywords, _ => Step(control, control.Step), group: group) { Family = numberFamily, FamilyRole = "Up" });
                    actions.Add(new HotkeyAction($"{id}.down", $"{name} down", title, control.Keywords, _ => Step(control, -control.Step), group: group) { Family = numberFamily, FamilyRole = "Down" });
                    break;
                case PointerKind.Switch:
                    actions.Add(new HotkeyAction($"{id}.toggle", $"Toggle {control.ActionName}", title, control.Keywords, _ => Toggle(control), group: group));
                    break;
                case PointerKind.Choice:
                    var choiceFamily = new ActionFamily(id, control.Label);
                    actions.Add(new HotkeyAction($"{id}.next", $"Next {control.ActionName}", title, control.Keywords, _ => StepScheme(+1), group: group) { Family = choiceFamily, FamilyRole = "Next" });
                    actions.Add(new HotkeyAction($"{id}.previous", $"Previous {control.ActionName}", title, control.Keywords, _ => StepScheme(-1), group: group) { Family = choiceFamily, FamilyRole = "Previous" });
                    break;
            }
        }
        actions.Add(new HotkeyAction("pointer.reset", "Reset pointer settings", title, Keywords + " reset defaults", _ => ResetToDefaults(), group: group));
        actions.Add(new HotkeyAction("pointer.restore", "Restore original pointer settings", title, Keywords + " restore original undo", _ => RestoreOriginal(), group: group));
        hotkeys.Register(actions);
    }

    /// An action moves a number by <paramref name="delta"/> of its own units; a setting that does not apply now is left alone, with the reason.
    private void Step(PointerControl control, int delta)
    {
        if (control.Needs is { } needs && _pointer.Read(needs.Value) != (needs.On ? 1 : 0))
        {
            _report(needs.Reason);
            return;
        }
        if (_pointer.Step(control.Value!.Value, delta) is not { } now) return;
        if (control.Osd is { } element) _osd.Show(element, Percent.OfRange(now, control.Range.Min, control.Range.Max));
        Changed?.Invoke();
    }

    /// An action flips a switch from what Windows has now (a button swap still asks to be kept).
    private void Toggle(PointerControl control)
    {
        var value = control.Value!.Value;
        if (_pointer.Read(value) is not { } now) return;
        if (value == PointerValue.Swap)
        {
            AskKeepSwap(now != 1);
            return;
        }
        _pointer.Set(value, now == 1 ? 0 : 1);
        Changed?.Invoke();
    }

    private void StepScheme(int direction)
    {
        var schemes = _pointer.Schemes();
        if (schemes.Count == 0) return;
        var at = _pointer.CurrentScheme(schemes) is { } current ? schemes.ToList().IndexOf(current) : -1;
        var next = at < 0 ? (direction > 0 ? 0 : schemes.Count - 1) : Wrap.Index(at + direction, schemes.Count);
        ApplyScheme(schemes[next]);
    }

    /// Uses a pointer scheme (its card or an action).
    public void ApplyScheme(CursorScheme scheme) => Guard.Fire("Mouse: pointer scheme", async () =>
    {
        if (!await _pointer.ApplyScheme(scheme)) _report($"Windows didn't accept the pointer scheme {scheme.Name}, so the pointers stay as before.");
        Changed?.Invoke();
    }, _report);

    /// Swaps the buttons (or swaps them back) on trial and asks whether to keep it. The message has Keep focused, so Enter keeps it
    /// even when the clicking button is the unfamiliar one; anything else, or no answer within <see cref="Timing.PointerSwapKeep"/>, puts it back.
    public void AskKeepSwap(bool swapped) => Guard.Fire("Mouse: button swap", async () =>
    {
        var trying = _pointer.ChangeSwap(swapped);
        Changed?.Invoke();
        if (trying is not { } trial) return;
        var seconds = (int)Timing.PointerSwapKeep.TotalSeconds;
        var now = swapped ? "The right button now clicks." : "The left button now clicks.";
        var keep = await _ask(new DialogMessage("Keep this button swap?",
            $"{now} It goes back in {seconds} seconds unless you keep it.", "Keep", Dismiss: trial));
        if (keep) _pointer.KeepSwap();
        else _pointer.PutBackSwap();
    }, _report);

    public void ResetToDefaults() => Guard.Fire("Mouse: pointer defaults", async () =>
    {
        var ok = await _pointer.ResetToDefaults();
        Changed?.Invoke();
        if (!ok) _report("Windows didn't accept every default, so the cards show what it has now.");
        AskKeepSwap(PointerValues.Info(PointerValue.Swap).Default == 1);
    }, _report);

    public void RestoreOriginal() => Guard.Fire("Mouse: original pointer settings", async () =>
    {
        if (!_pointer.HasOriginal) return;
        var ok = await _pointer.RestoreOriginal();
        Changed?.Invoke();
        if (!ok) _report("Windows didn't accept every original value, so the cards show what it has now.");
    }, _report);

    /// Windows refused a write: say so in one sentence; the page shows what Windows really has.
    private void OnWriteFailed(PointerValue value)
    {
        var name = PointerControls.All.First(c => c.Value == value).ActionName;
        _report($"Windows didn't accept the change to {name}, so it stays as before.");
        Changed?.Invoke();
    }

    public void Dispose() => _pointer.WriteFailed -= OnWriteFailed;
}
