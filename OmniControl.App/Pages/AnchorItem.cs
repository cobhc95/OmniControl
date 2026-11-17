using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>A card that picks one square of a 3 x 3 grid (the OSD placement window's anchor): <see cref="Index"/> 0 to 8, row by row from the top left. Calls the apply action when changed.</summary>
public sealed partial class AnchorItem : ObservableObject, IEditorItem
{
    private readonly Action<int> _apply;
    private bool _loading;

    public string Label { get; }

    [ObservableProperty] private int _index;

    public AnchorItem(string label, int index, Action<int> apply) => (Label, _index, _apply) = (label, index, apply);

    /// <summary>Shows a square chosen elsewhere (a repin) without applying it.</summary>
    public void Load(int index)
    {
        _loading = true;
        Index = index;
        _loading = false;
    }

    partial void OnIndexChanged(int value)
    {
        if (!_loading) _apply(value);
    }
}
