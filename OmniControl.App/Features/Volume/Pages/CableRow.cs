using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// One row of the Wiring tab's Virtual cables list: a virtual device found on this PC (a cable pair, a cable end with no other end, or
/// a virtual device of another kind), its switch (OmniControl may use it or not), what Windows shows of it, whether a run uses it, and
/// what it is used for or why it is not. The page fills it from what the PC offers and the engine's plan; this class decides nothing.
public sealed partial class CableRow : ObservableObject, IEditorItem
{
    private readonly Action<CableRow, bool> _allow;
    private readonly Action<CableRow> _remove;

    /// <param name="allow">The switch moved: the page stores the choice by <see cref="Id"/>.</param>
    /// <param name="remove">The x of a cable the user marked: the page forgets the mark.</param>
    public CableRow(string id, Action<CableRow, bool> allow, Action<CableRow> remove)
    {
        Id = id;
        _allow = allow;
        _remove = remove;
    }

    /// The cable's stable id (<see cref="Core.Engine.FoundCable.Id"/>), under which its switch is stored.
    public string Id { get; }

    [ObservableProperty] private string _name = string.Empty;
    string IEditorItem.Label => Name;

    /// Who makes it and what it is (the name's tooltip).
    [ObservableProperty] private string _tip = string.Empty;

    /// "Input + Output", "Input only", "Output only", "Not plugged in", "Mixer".
    [ObservableProperty] private string _endsTag = string.Empty;

    /// "Free" (good) or "In use"; empty for a cable that is not used.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasState))] private string _state = string.Empty;
    [ObservableProperty] private bool _isFree;
    public bool HasState => State.Length > 0;

    /// "Use for: master bus", "Use for: Automatic", or "Not used: no matching output".
    [ObservableProperty] private string _use = string.Empty;
    [ObservableProperty] private string _useTip = string.Empty;

    /// The switch's tooltip: what it does, and what stops if it is switched off while a run uses the cable.
    [ObservableProperty] private string _switchTip = EffectsRules.CableSwitchTip(null);

    /// The switch can be moved (only a whole cable pair can be used).
    [ObservableProperty] private bool _canSwitch;

    /// A cable the user marked by hand: it has an x that forgets it.
    [ObservableProperty] private bool _canRemove;

    /// Dimmed while OmniControl does not use it (switched off, or not a usable pair).
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RowOpacity))] private bool _isQuiet;
    public double RowOpacity => IsQuiet ? VolumeRow.QuietOpacity : 1;

    private bool _loading;

    /// On = OmniControl may use this cable. Set by the page without storing (<see cref="LoadAllowed"/>); a user's move is stored.
    [ObservableProperty] private bool _isAllowed;

    public void LoadAllowed(bool allowed)
    {
        _loading = true;
        IsAllowed = allowed;
        _loading = false;
    }

    partial void OnIsAllowedChanged(bool value)
    {
        if (!_loading) _allow(this, value);
    }

    [RelayCommand] private void Remove() { if (CanRemove) _remove(this); }
}
