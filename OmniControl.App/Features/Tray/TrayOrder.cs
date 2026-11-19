namespace OmniControl.App.Features.Tray;

/// The stored list's side of an edit made on one shown slider or tile (pure, tested): a drag to another place, a remove, a new action or
/// icon. An element with a <c>*</c> shows one item per match, so before one of its items is edited alone the element is spelt out: one
/// element per shown match, in place, followed by the pattern itself, which then shows only matches that appear later (the shown items
/// of a list never repeat an id it names on its own, <see cref="TrayTargets"/>). Elements not shown now (an id not registered) keep their place.
public static class TrayOrder
{
    /// The item at shown index <paramref name="from"/> goes to shown index <paramref name="to"/>; the others keep their order.
    public static List<TrayElement> Move(IReadOnlyList<TrayElement> list, IReadOnlyList<TrayTarget> shown, int from, int to)
    {
        var (elements, items) = Expanded(list, shown);
        if (from < 0 || from >= items.Count) return elements;
        var moving = items[from];
        var others = items.Where((_, i) => i != from).ToList();
        elements.Remove(moving);
        var at = others.Count == 0 ? elements.Count
            : to >= others.Count ? elements.IndexOf(others[^1]) + 1
            : elements.IndexOf(others[Math.Max(0, to)]);
        elements.Insert(at, moving);
        return elements;
    }

    /// The item at <paramref name="index"/> goes; an item of a pattern takes the pattern with it, so it does not come back.
    public static List<TrayElement> Remove(IReadOnlyList<TrayElement> list, IReadOnlyList<TrayTarget> shown, int index)
    {
        var (elements, items) = Expanded(list, shown);
        if (index < 0 || index >= items.Count) return elements;
        elements.Remove(items[index]);
        if (index < shown.Count && IsPattern(shown[index].Element)) elements.Remove(shown[index].Element);
        return elements;
    }

    /// The item at <paramref name="index"/> becomes <paramref name="with"/> in its place, keeping the icon the user gave it.
    public static List<TrayElement> Replace(IReadOnlyList<TrayElement> list, IReadOnlyList<TrayTarget> shown, int index, TrayElement with)
    {
        var (elements, items) = Expanded(list, shown);
        if (index < 0 || index >= items.Count) return elements;
        var replacement = with.Copy();
        replacement.Icon = items[index].Icon;
        if (replacement.Choice is null && replacement.State == items[index].State) replacement.Choice = items[index].Choice;
        elements[elements.IndexOf(items[index])] = replacement;
        return elements;
    }

    /// The item at <paramref name="index"/> gets <paramref name="icon"/> (null: its own default) without changing the others of a pattern.
    public static List<TrayElement> WithIcon(IReadOnlyList<TrayElement> list, IReadOnlyList<TrayTarget> shown, int index, string? icon)
    {
        var (elements, items) = Expanded(list, shown);
        if (index >= 0 && index < items.Count) items[index].Icon = icon;
        return elements;
    }

    /// The list with every pattern that shows items spelt out, and the element that stands for each shown item, in the shown order.
    public static (List<TrayElement> Elements, List<TrayElement> Items) Expanded(IReadOnlyList<TrayElement> list, IReadOnlyList<TrayTarget> shown)
    {
        var elements = new List<TrayElement>();
        var items = new List<TrayElement>();
        foreach (var element in list)
        {
            var mine = shown.Where(t => ReferenceEquals(t.Element, element)).ToList();
            if (IsPattern(element) && mine.Count > 0)
            {
                var spelt = mine.Select(Spelt).ToList();
                elements.AddRange(spelt);
                items.AddRange(spelt);
                elements.Add(element);
                continue;
            }
            elements.Add(element);
            items.AddRange(mine.Select(_ => element));
        }
        return (elements, items);
    }

    private static bool IsPattern(TrayElement element) => !element.Together && (IdPattern.IsPattern(element.Action ?? string.Empty) || IdPattern.IsPattern(element.State ?? string.Empty));

    /// One shown match of a pattern as an element of its own (the ids it found, the pattern's choice and icon).
    private static TrayElement Spelt(TrayTarget target) => new()
    {
        Action = target.Element.Action is null ? null : target.Action?.Id ?? target.Element.Action,
        State = target.Element.State is null ? null : target.State?.Id ?? target.Element.State,
        Choice = target.Element.Choice,
        AllState = target.Element.AllState,
        Icon = target.Element.Icon,
    };
}
