using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>One box of a card's picture, with whether an arrow comes before it, and its colour as flags a style can read.</summary>
public sealed record PictureStep(string Text, PictureTone Tone, bool ArrowBefore)
{
    public bool IsApo => Tone == PictureTone.Apo;
    public bool IsOwn => Tone == PictureTone.Own;

    /// <summary>The boxes of a picture from its parts, an arrow before every box but the first (the goal cards and the setup screens draw them alike).</summary>
    public static IReadOnlyList<PictureStep> Of(IReadOnlyList<PicturePart> parts) => parts.Select((p, i) => new PictureStep(p.Text, p.Tone, i > 0)).ToList();
}

/// <summary>One card of the welcome screen: a goal, whether it is already on (shown "✓ on"), and what choosing it does.</summary>
public sealed class GoalCard
{
    public GoalCard(EasyGoal goal, bool isOn, Action<EasyGoal> choose)
    {
        Goal = goal;
        IsOn = isOn;
        ChooseCommand = new RelayCommand(() => choose(goal));
        Steps = PictureStep.Of(goal.Picture);
    }

    public IReadOnlyList<PictureStep> Steps { get; }

    public EasyGoal Goal { get; }
    public bool IsOn { get; }
    public IRelayCommand ChooseCommand { get; }
}
