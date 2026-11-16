using OmniControl.App.Mouse;
using OmniControl.App.Services;

namespace OmniControl.App.Picker;

/// <summary>
/// The entries the user used last in the Choose-the-action window (the Recent section), newest first, kept in recent-actions.json. Only the
/// entry's id is kept, so an entry that is gone is simply not listed; its settings stay with the button, gesture or rule that uses it.
/// </summary>
public static class Recents
{
    private static readonly DataStore Store = DataStores.RecentActions;
    private const int Keep = 8;

    /// <summary>Puts this entry first (it is not remembered twice) and forgets the oldest beyond <see cref="Keep"/>.</summary>
    public static void Remember(string spec)
    {
        var list = (AppDataStore.Load<List<string>>(Store) ?? new()).Where(s => s != spec).Prepend(spec).Take(Keep).ToList();
        AppDataStore.Save(Store, list);
    }

    /// <summary>The remembered entries that are in <paramref name="choices"/> and can be used now, newest first.</summary>
    public static List<PickChoice> Matching(IEnumerable<PickChoice> choices)
    {
        var usable = choices.Where(c => c.Command is null && c.Unavailable is null).ToList();
        return (AppDataStore.Load<List<string>>(Store) ?? new())
            .Select(spec => usable.FirstOrDefault(c => c.Spec == spec)).OfType<PickChoice>().ToList();
    }
}
