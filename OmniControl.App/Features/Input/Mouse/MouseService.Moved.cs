using OmniControl.App.Features.Hotkeys.Actions;

namespace OmniControl.App.Features.Input.Mouse;

// Mappings follow an action whose id moved (a merged smart device's actions, HotkeyRegistry.Moved): the stored text is rewritten here,
// by the owner of mouse.json, and applied like any other change.
public sealed partial class MouseService
{
    /// Every mapping (first and further actions, every layer) that names an old id now names the new one.
    public void FollowMoved(IReadOnlyDictionary<string, string> moved)
    {
        var changed = false;
        foreach (var (key, stored) in Settings.Bindings.ToList())
        {
            var now = MovedActions.RewriteAction(stored, moved);
            if (now == stored) continue;
            Settings.Bindings[key] = now;
            changed = true;
        }
        foreach (var extras in Settings.ExtraActions.Values) changed |= MovedActions.RewriteAll(extras, moved);
        if (changed) Apply();
    }
}
