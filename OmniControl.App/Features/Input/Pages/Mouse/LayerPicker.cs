using OmniControl.App.Features.Input.Mouse;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// One family's layer drop-down on a group's title line (Mouse Buttons, Trackpad): the layer being edited, which is also the layer in
/// force, so the drop-down doubles as a live indicator. Picking a layer makes it the one in force; a layer change from a button,
/// gesture or hotkey is shown without being sent back.
internal sealed class LayerPicker
{
    private readonly MouseService _mouse;
    private readonly InputFamily _family;
    private readonly Action _reloadCards;
    private bool _refreshing;
    private IReadOnlyList<int> _listed = Array.Empty<int>();

    /// The bare drop-down (its label is what search results show).
    public ChoiceItem Choice { get; }

    /// The layer the family's cards show and edit.
    public int Layer { get; private set; }

    /// <param name="reloadCards">Shows the family's cards on the new layer.</param>
    /// <param name="manage">The chip's "Manage presets…": opens the preset manager at the family's layers.</param>
    public LayerPicker(MouseService mouse, InputFamily family, string label, Action reloadCards, Action manage)
    {
        _mouse = mouse;
        _family = family;
        _reloadCards = reloadCards;
        Choice = new ChoiceItem(label, new[] { string.Empty }, string.Empty, OnPicked) { Manage = manage };
        Layer = mouse.LayerOf(family);
        Refresh();
    }

    /// The service says the family's layer in force changed: show it and its cards.
    public void Follow(int layer)
    {
        Layer = layer;
        Refresh();
        _reloadCards();
    }

    /// The layer list and the name shown, from the settings and the layer being shown.
    public void Refresh()
    {
        var layers = _mouse.Settings.LayersOf(_family);
        var labels = layers.Labels();
        _listed = layers.Listed(Layer, _mouse.LayerOf(_family));
        _refreshing = true;
        Choice.Options = _listed.Select(n => labels[n]).ToList();
        Choice.Selected = labels[Layer];
        _refreshing = false;
    }

    private void OnPicked(string? text)
    {
        if (_refreshing || text is null) return;
        var index = Choice.Options.ToList().IndexOf(text);
        if (index < 0 || index >= _listed.Count) return;
        Layer = _listed[index];
        _reloadCards();
        _mouse.SetLayer(_family, Layer);
    }
}
