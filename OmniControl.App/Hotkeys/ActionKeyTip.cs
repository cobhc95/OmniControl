using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Hotkeys;

/// <summary>
/// A tooltip text that carries an action's current keys, "Open OmniControl (Alt+X)", read live from the master list: it follows the action's keys,
/// a changed hotkey set and an action registered later. With no key (or no such action) the text is just the name.
/// </summary>
public sealed partial class ActionKeyTip : ObservableObject, IDisposable
{
    private readonly HotkeyRegistry _hotkeys;
    private readonly string _actionId;
    private readonly string _name;
    private HotkeyAction? _action;

    [ObservableProperty] private string _text;

    public ActionKeyTip(HotkeyRegistry hotkeys, string actionId, string name)
    {
        (_hotkeys, _actionId, _name) = (hotkeys, actionId, name);
        _text = name;
        _hotkeys.Actions.CollectionChanged += OnActionsChanged;
        Follow();
    }

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Follow();

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HotkeyAction.Keys)) Read();
    }

    private void Follow()
    {
        var found = _hotkeys.Find(_actionId);
        if (ReferenceEquals(found, _action)) return;
        if (_action is not null) _action.PropertyChanged -= OnActionChanged;
        _action = found;
        if (_action is not null) _action.PropertyChanged += OnActionChanged;
        Read();
    }

    private void Read()
    {
        var keys = string.Join("+", Keycaps.Summary(_action?.Keys ?? Array.Empty<string>()).Select(p => p.Text));
        Text = keys.Length == 0 ? _name : $"{_name} ({keys})";
    }

    public void Dispose()
    {
        _hotkeys.Actions.CollectionChanged -= OnActionsChanged;
        if (_action is not null) _action.PropertyChanged -= OnActionChanged;
    }
}
