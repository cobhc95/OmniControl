namespace OmniControl.App.Features.Hotkeys;

/// THE suggested-keys switch: a muted "Suggested keys" label with a plain switch, the keys of the set drawn as
/// keycaps under it, and a caution tag per action whose suggested key another action holds. It works for any set of actions
/// (<see cref="HotkeyAction.SuggestedKeySet"/>); the rules and the state belong to <see cref="HotkeyRegistry"/>, this only shows and asks. It
/// follows the registry's saves, so a hand edit of one of the keys turns the switch off here at once.
public sealed partial class SuggestedKeysSwitch : ObservableObject, IDisposable
{
    private readonly HotkeyRegistry _registry;
    private readonly string _setId;
    private bool _syncing;

    public string Label => "Suggested keys";

    [ObservableProperty] private bool _isOn;

    /// The keys the switch gives, as one short row of keycaps.
    [ObservableProperty] private IReadOnlyList<KeycapPart> _keys = Array.Empty<KeycapPart>();

    /// Each key and what it does: the switch's tooltip.
    [ObservableProperty] private string _tip = string.Empty;

    /// One caution tag per member that went without its key because another action has it.
    public ObservableCollection<TagItem> Tags { get; } = new();

    public SuggestedKeysSwitch(HotkeyRegistry registry, string setId)
    {
        (_registry, _setId) = (registry, setId);
        registry.BindingsSaved += Refresh;
        Refresh();
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_syncing) _registry.SetSuggestedKeys(_setId, value);
    }

    private void Refresh()
    {
        var members = _registry.SuggestedMembers(_setId);
        _syncing = true;
        IsOn = _registry.SuggestedKeysOn(_setId);
        _syncing = false;
        Keys = Keycaps.Summary(members.Select(m => m.SuggestedKey!));
        Tip = string.Join("\n", members.Select(m => $"{m.SuggestedKey}: {m.Title}"));
        Tags.Clear();
        if (!IsOn) return;
        foreach (var member in members)
            if (_registry.SuggestedKeyHeldBy(member) is { } holder)
                Tags.Add(new TagItem($"Also on: {holder.Title}", TagTone.Caution, $"{member.SuggestedKey} stays with {holder.Title}, so {member.Title} has no key."));
    }

    public void Dispose() => _registry.BindingsSaved -= Refresh;
}
