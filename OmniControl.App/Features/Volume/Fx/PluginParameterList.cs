namespace OmniControl.App.Features.Volume.Fx;

/// A plug-in's settings as OmniControl's own plain list (for small screens, and for plug-ins without an editor of their own):
/// one card per group of the plug-in (its units), one slider row per setting; a search box when there are more than
/// <see cref="SearchFrom"/>. Whoever opened the plug-in for it (<see cref="EffectEditorDock"/>) closes it; after <see cref="Close"/>
/// the list no longer touches the plug-in.
public sealed partial class PluginParameterList : ObservableObject
{
    /// More settings than this get a search box.
    private const int SearchFrom = 12;

    private readonly IReadOnlyList<PluginParamItem> _all;
    private bool _closed;

    public PluginParameterList(IPluginEffect effect)
    {
        _all = effect.Parameters.Select(p => new PluginParamItem(effect, p, RefreshAll, () => !_closed)).ToList();
        Groups = GroupsOf(_all);
    }

    /// The cards shown: every group, or the settings that match the search.
    [ObservableProperty] private IReadOnlyList<PluginParamGroup> _groups = Array.Empty<PluginParamGroup>();

    /// Words to find settings by (name or group).
    [ObservableProperty] private string _search = string.Empty;

    /// This many settings or more make a long list: one card per setting (the first of a group carries its name), in a scrolling
    /// list that builds only the cards in view (<see cref="EffectEditorView"/>).
    public const int LongFrom = 40;

    public bool HasSearch => _all.Count > SearchFrom;

    public bool IsLong => _all.Count >= LongFrom;

    partial void OnSearchChanged(string value)
    {
        var words = value.Trim();
        Groups = GroupsOf(words.Length == 0 ? _all : _all.Where(p => p.Matches(words)).ToList());
    }

    /// The plug-in is closed for this list: nothing reads or writes it any more.
    public void Close() => _closed = true;

    /// A setting changed: a plug-in may move others with it, so every setting is read again.
    private void RefreshAll()
    {
        if (_closed) return;
        foreach (var p in _all) p.Refresh();
    }

    private IReadOnlyList<PluginParamGroup> GroupsOf(IReadOnlyList<PluginParamItem> items) =>
        items.GroupBy(p => p.Group).SelectMany(g => IsLong
            ? g.Select((p, i) => new PluginParamGroup(i == 0 ? g.Key : string.Empty, new[] { p }))
            : new[] { new PluginParamGroup(g.Key, g.ToList()) }).ToList();
}

/// One card of the settings list: the plug-in's group (unit) name, empty for settings of the plug-in as a whole, and its settings.
public sealed record PluginParamGroup(string Name, IReadOnlyList<PluginParamItem> Items)
{
    public bool HasName => Name.Length > 0;
}
