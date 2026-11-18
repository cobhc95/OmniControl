namespace OmniControl.App.Pages;

/// <summary>
/// A group header's master switch that is not made of the group's own on/off cards: what it shows (true on, false off, null mixed or not known)
/// and what a click does; <c>Count</c> (screens on, screens listed; null: not counted) gives the group its "N of M on" summary like a group of switches. <see cref="GroupMasterSwitch"/> draws it for every group the same way; <see cref="CardGroup.UseSwitch"/> sets it.
/// </summary>
public sealed record GroupSwitch(Func<bool?> State, Action Toggle, Func<(int On, int Total)?>? Count = null);

/// <summary>
/// The screen power switches of a Displays group, made once for the local Displays page and the Other PCs view: one screen's switch, and the
/// master of all screens (all on becomes all off, anything else all on; off goes through <c>allOff</c>, the "Turn off displays" action, when some
/// screen has no way of its own to be switched).
/// </summary>
public static class ScreenPowerSwitch
{
    /// <summary>One screen: whether it is on (null: not known) and how it is switched.</summary>
    public sealed record Screen(Func<bool?> IsOn, Action<bool> SetOn);

    public static GroupSwitch One(Screen screen) => new(screen.IsOn, () => screen.SetOn(screen.IsOn() != true));

    /// <param name="screens">The screens that can be switched one by one, read when the switch is shown or clicked.</param>
    /// <param name="everyScreenCovered">True when those are all the screens there are (else turning off needs <paramref name="allOff"/>).</param>
    /// <param name="allOff">Turns every screen off at once; null: none.</param>
    /// <param name="knownOff">True when every screen is known to be off even if some cannot be switched one by one (Windows said so, or every listed screen reads off); the switch then shows off.</param>
    public static GroupSwitch All(Func<IReadOnlyList<Screen>> screens, Func<bool> everyScreenCovered, Action? allOff, Func<bool>? knownOff = null) => new(
        () =>
        {
            if (knownOff?.Invoke() == true) return false;
            var list = screens();
            if (list.Count == 0) return null;
            var on = list.Count(s => s.IsOn() == true);
            return on == list.Count ? true : on == 0 && everyScreenCovered() ? false : null;
        },
        () =>
        {
            var list = screens();
            var turnOn = list.Count == 0 ? false : !(list.All(s => s.IsOn() == true));
            if (turnOn)
            {
                foreach (var screen in list) screen.SetOn(true);
            }
            else if (allOff is not null && (list.Count == 0 || !everyScreenCovered())) allOff();
            else foreach (var screen in list) screen.SetOn(false);
        },
        () =>
        {
            var list = screens();
            return list.Count == 0 ? null : (knownOff?.Invoke() == true ? 0 : list.Count(s => s.IsOn() == true), list.Count);
        });
}
