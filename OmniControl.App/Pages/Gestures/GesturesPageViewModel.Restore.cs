using OmniControl.App.Pages.Restore;

namespace OmniControl.App.Pages.Gestures;

/// <summary>
/// "Restore defaults" on the Gestures page (<see cref="GestureRestoreState"/>): the built-in gestures go back through the gesture service and
/// the custom rules are deleted through the rule service; the question carries a stronger line when rules exist, and the undo brings them back.
/// </summary>
public sealed partial class GesturesPageViewModel
{
    private IRestorable? _restorable;

    public override IRestorable? Restorable => _restorable ??= new SettingsRestore<GestureRestoreState>("gestures", Title,
        () => GestureRestoreState.Scope,
        () => new GestureRestoreState { Gestures = _gestures.Settings, Rules = _rules.Settings },
        GestureRestoreState.Defaults, ApplyState, GestureRestoreState.Same,
        warning: () => GestureRestoreState.WarningOf(_rules.Settings.Rules.Count));

    private Task<bool> ApplyState(GestureRestoreState target)
    {
        _gestures.Replace(target.Gestures);
        _rules.ReplaceAll(target.Rules);
        _enabled.Load(_gestures.Settings.Enabled);
        _edgeStrip.Load(_gestures.Settings.EdgeStrip);
        _margins.Refresh();
        AreasScope.Refresh();
        foreach (var item in _gestureItems) item.Reload();
        Rules.Rules.Clear();
        foreach (var rule in _rules.Settings.Rules) AddItem(rule);
        RefreshRules();
        return Task.FromResult(true);
    }
}
