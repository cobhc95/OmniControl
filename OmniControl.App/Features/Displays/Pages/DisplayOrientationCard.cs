using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Displays.Pages;

/// One display's Orientation card: a view of its <see cref="DisplayOrientationModel"/> as a gallery of the four ways Windows can turn it.
internal sealed class DisplayOrientationCard : IDisposable
{
    private readonly DisplayOrientationModel _model;
    private readonly SuggestedKeysSwitch _suggestedKeys;

    public GalleryItem Card { get; }

    /// <param name="suggestedKeys">The switch for the orientation hotkeys' suggested keys, shown at the card's right; the card owns it.</param>
    public DisplayOrientationCard(DisplayOrientationModel model, SuggestedKeysSwitch suggestedKeys)
    {
        (_model, _suggestedKeys) = (model, suggestedKeys);
        Card = new GalleryItem(DisplayOrientationModel.Label, DisplayOrientationModel.Names, model.State.Value, () => OrientationPreview.Build(Card!.Index), model.Set)
        {
            Tip = "Turns the screen through Windows. Its width and height swap between landscape and portrait.",
            Extra = suggestedKeys,
        };
        model.Changed += Show;
        Show();
    }

    private void Show()
    {
        if (Card.Index != _model.State.Value) Card.Load(_model.State.Value);
        CardMirror.Show(Card.Availability, _model.State.Reason);
    }

    public void Dispose()
    {
        _model.Changed -= Show;
        _suggestedKeys.Dispose();
    }
}
