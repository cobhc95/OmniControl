namespace OmniControl.App.Features.Volume.Pages.Easy;

/// One box of a card's picture, with whether an arrow comes before it, and its colour as flags a style can read.
public sealed record PictureStep(string Text, PictureTone Tone, bool ArrowBefore)
{
    public bool IsApo => Tone == PictureTone.Apo;
    public bool IsOwn => Tone == PictureTone.Own;

    /// The boxes of a picture from its parts, an arrow before every box but the first (the goal cards and the setup screens draw them alike).
    public static IReadOnlyList<PictureStep> Of(IReadOnlyList<PicturePart> parts) => parts.Select((p, i) => new PictureStep(p.Text, p.Tone, i > 0)).ToList();
}

/// One card of the welcome screen: a goal, whether it is already on (shown "✓ on") and whether it is ticked. Clicking anywhere on it toggles
/// the tick; which goals are ticked is the view-model's list (<see cref="EasyViewModel"/>), this only shows it and reports the click.
public sealed partial class GoalCard : ObservableObject
{
    public GoalCard(EasyGoal goal, bool isOn, Action<GoalCard> toggled)
    {
        Goal = goal;
        IsOn = isOn;
        ToggleCommand = new RelayCommand(() => toggled(this));
        Steps = PictureStep.Of(goal.Picture);
    }

    public IReadOnlyList<PictureStep> Steps { get; }

    public EasyGoal Goal { get; }
    public bool IsOn { get; }
    public IRelayCommand ToggleCommand { get; }

    /// Ticked: the setup will run for this goal (set by the view-model).
    [ObservableProperty] private bool _isSelected;
}
