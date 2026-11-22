using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Icons;

/// The one way every "Change icon…" opens the icon picker (<see cref="IconPickerWindow"/>) over the window it was asked from: the tray
/// panel's tiles and sliders, a smart device's icon, an action in the Actions and Hotkeys window. It answers with the chosen value, or
/// null for "Default"; the caller saves it where the item keeps its icon.
public sealed class IconPicker
{
    private readonly IconStore _store;

    public IconPicker(IconStore store) => _store = store;

    /// A user icon was added, renamed or removed, or an action's icon chosen: what shows icons draws them again.
    public event Action? Changed
    {
        add => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    /// The picker over the window that holds <paramref name="from"/>; null when it was cancelled (or no window could hold it).
    public async Task<IconPick?> ChooseAsync(Visual from, IconRequest request)
    {
        if ((TopLevel.GetTopLevel(from) as Window ?? OwnedWindow.MainWindow) is not { } owner) return null;
        var window = new IconPickerWindow(new IconPickerModel(_store, request)) { Topmost = owner.Topmost };
        UiTiming.WhenOpened(window, "Icon picker", System.Diagnostics.Stopwatch.StartNew());
        return await window.ShowDialog<IconPick?>(owner);
    }

    /// "Change icon…" of an action: its suggestions, and the answer kept as the action's chosen icon.
    public async Task ChooseForActionAsync(Visual from, HotkeyAction action)
    {
        var request = new IconRequest(action.Title, action.ShownIcon, IconLibrary.Suggestions(action.SuggestedIcons, action.Icon, action.Title));
        if (await ChooseAsync(from, request) is { } pick) _store.ChooseForAction(action, pick.Value);
    }
}
