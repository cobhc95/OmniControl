using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;

namespace OmniControl.App.Picker;

/// <summary>
/// The settings of an OmniControl action in the one window (docs/mockups/unified-action-window.html). The action's own settings (a program,
/// how far a level moves, a countdown) are its defaults, kept with its keys (<see cref="HotkeyAction.Defaults"/>) and used by its hotkeys and
/// rules. Opened from the menu they are edited here, live. Opened for a mouse button, gesture or rule step, the first row, "Same as the hotkey's
/// settings", is on unless the input keeps its own copy: while on, the rows show and edit the defaults; off, they are the input's own copy.
/// What only an input has (when it runs, how often) is always the input's.
/// </summary>
public sealed partial class PickerModel
{
    private const string SameKey = "$same";
    private const string DefaultsHeadingKey = "$defaults";

    private HotkeyAction? _defaultsOf; // the master action whose defaults the settings pane shows; null when the entry has none
    private HashSet<string> _ownKeys = new();
    private bool _same; // pick mode: the input uses the action's defaults

    /// <summary>The name of what the window picks for ("Middle button", "Step 2"): the confirm button says "Use for ...". Null: "Use this action".</summary>
    public string? Target { get; set; }

    private bool IsOwnSetting(OptionItem item) => _defaultsOf is not null && _ownKeys.Contains(item.Option.Key);

    /// <summary>Finds what the entry's settings pane edits as defaults; returns the values the own settings read from (the defaults, or the input's own copy).</summary>
    private IReadOnlyDictionary<string, string> ReadDefaults(PickChoice choice, IReadOnlyDictionary<string, string> values)
    {
        _defaultsOf = null;
        _ownKeys = new HashSet<string>();
        _same = false;
        if (_manage is not { } keys || keys.ActionOf(choice) is not { } action || choice.Editor is not null) return values;
        var own = keys.Registry.Settings?.OptionsOf(action).Where(o => o.Kind is not (OptionKind.Heading or OptionKind.Panel)).Select(o => o.Key).ToHashSet();
        if (own is not { Count: > 0 }) return values;
        _defaultsOf = action;
        _ownKeys = own;
        var keepsCopy = values.ContainsKey(OptionValues.OwnKey) || own.Any(values.ContainsKey);
        _same = _picking && !(choice == _current && keepsCopy);
        return IsManaging || _same ? action.Defaults : values;
    }

    /// <summary>The first row of the settings: the heading of the defaults (menu), or the switch (an input).</summary>
    private void AddDefaultsRow()
    {
        if (_defaultsOf is null) return;
        _allOptions.Insert(0, IsManaging
            ? new OptionItem(ActionOption.Heading(DefaultsHeadingKey, "Settings · used by hotkeys and rules"), string.Empty)
            : new OptionItem(ActionOption.Toggle(SameKey, "Same as the hotkey's settings", on: true), _same ? "1" : "0"));
    }

    /// <summary>True when a changed row was handled here (the defaults were written, or the switch flipped); false for the input's own rows.</summary>
    private bool TakeDefaultsChange(OptionItem item)
    {
        if (item.Option.Key == SameKey)
        {
            SwitchSame(item.IsOn);
            return true;
        }
        if (!IsOwnSetting(item) || !(IsManaging || _same)) return false;
        WriteDefaults();
        SyncVisibleOptions();
        return true;
    }

    private void WriteDefaults()
    {
        var own = _allOptions.Where(IsOwnSetting).ToList();
        var values = OptionValues.NonDefault(own.Select(i => i.Option).ToList(), own.ToDictionary(i => i.Option.Key, i => i.Value));
        _defaultsOf!.SetDefaults(values);
        foreach (var other in FamilyActions().Where(a => a != _defaultsOf)) other.SetDefaults(values); // the directions of a family share their settings
    }

    /// <summary>The switch was flipped: on, the rows show the defaults again; off, the input starts its own copy from them. Either way the pick is live at once.</summary>
    private void SwitchSame(bool on)
    {
        _same = on;
        if (on)
        {
            _syncingEditor = true;
            foreach (var item in _allOptions.Where(IsOwnSetting))
                item.Value = _defaultsOf!.Defaults.TryGetValue(item.Option.Key, out var value) ? value : item.Option.Default;
            _syncingEditor = false;
        }
        SyncVisibleOptions();
        if (_editing is not null && _editing == _current) Live?.Invoke(Gather(_editing));
    }

    /// <summary>The settings the input stores: without its own copy, none of the action's own; with one, all of them and the mark that says so.</summary>
    private Dictionary<string, string> WithOwnCopy(Dictionary<string, string> settings)
    {
        if (_defaultsOf is null) return settings;
        if (_same) return settings.Where(p => !_ownKeys.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        settings[OptionValues.OwnKey] = "1";
        return settings;
    }
}
