using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.Core.Engine;

namespace OmniControl.App.Fx;

/// <summary>
/// A plug-in's settings as OmniControl's own plain list (for small screens, and for plug-ins without an editor of their own):
/// one card per group of the plug-in (its units), one slider row per setting; a search box when there are more than
/// <see cref="SearchFrom"/>. Whoever opened the plug-in for it (<see cref="EffectEditorDock"/>) closes it; after <see cref="Close"/>
/// the list no longer touches the plug-in.
/// </summary>
public sealed partial class PluginParameterList : ObservableObject
{
    /// <summary>More settings than this get a search box.</summary>
    private const int SearchFrom = 12;

    private readonly IReadOnlyList<PluginParamItem> _all;
    private bool _closed;

    public PluginParameterList(IPluginEffect effect)
    {
        _all = effect.Parameters.Select(p => new PluginParamItem(effect, p, RefreshAll, () => !_closed)).ToList();
        Groups = GroupsOf(_all);
    }

    /// <summary>The cards shown: every group, or the settings that match the search.</summary>
    [ObservableProperty] private IReadOnlyList<PluginParamGroup> _groups = Array.Empty<PluginParamGroup>();

    /// <summary>Words to find settings by (name or group).</summary>
    [ObservableProperty] private string _search = string.Empty;

    public bool HasSearch => _all.Count > SearchFrom;

    partial void OnSearchChanged(string value)
    {
        var words = value.Trim();
        Groups = GroupsOf(words.Length == 0 ? _all : _all.Where(p => p.Matches(words)).ToList());
    }

    /// <summary>The plug-in is closed for this list: nothing reads or writes it any more.</summary>
    public void Close() => _closed = true;

    /// <summary>A setting changed: a plug-in may move others with it, so every setting is read again.</summary>
    private void RefreshAll()
    {
        if (_closed) return;
        foreach (var p in _all) p.Refresh();
    }

    private static IReadOnlyList<PluginParamGroup> GroupsOf(IReadOnlyList<PluginParamItem> items) =>
        items.GroupBy(p => p.Group).Select(g => new PluginParamGroup(g.Key, g.ToList())).ToList();
}

/// <summary>One card of the settings list: the plug-in's group (unit) name, empty for settings of the plug-in as a whole, and its settings.</summary>
public sealed record PluginParamGroup(string Name, IReadOnlyList<PluginParamItem> Items)
{
    public bool HasName => Name.Length > 0;
}
