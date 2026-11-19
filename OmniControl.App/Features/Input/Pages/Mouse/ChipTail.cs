using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// The accent "+N more" chip after a card's action chips: shown only while the chips fill their two lines (the row's panel decides
/// that and reports how many it left out); it opens the list of every action.
public sealed partial class FoldChip : ObservableObject
{
    [ObservableProperty] private string _text = string.Empty;

    /// The chip's words for <paramref name="hidden"/> chips left out.
    internal void Show(int hidden) => Text = $"+{hidden} more";
}

/// The dashed "+ add action" chip at the end of a card's chips: it opens the picker for a new last action.
public sealed partial class AddChip : ObservableObject, IPickerCard
{
    public const string Text = "+ add action";

    private readonly Func<PickerModel> _menu;
    private readonly Action<PickResult> _add;

    [ObservableProperty] private bool _canAdd;
    [ObservableProperty] private string _tip = string.Empty;
    [ObservableProperty] private string _label = string.Empty;

    internal AddChip(Func<PickerModel> menu, Action<PickResult> add)
    {
        _menu = menu;
        _add = add;
    }

    public PickerModel MenuFor(bool startInSettings) => _menu();

    public void Pick(PickResult pick) => _add(pick);
}
