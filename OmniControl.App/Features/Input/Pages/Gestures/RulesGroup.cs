namespace OmniControl.App.Features.Input.Pages.Gestures;

/// The Mouse and Trackpad page's "Custom gesture rules" group: one line per rule; its header says how many there are.
public sealed class RulesGroup : CardGroup
{
    public ObservableCollection<RuleItem> Rules { get; } = new();

    public RulesGroup() : base("Gestures/Rules", "Custom gesture rules", CountText(0))
    {
        Tip = "The first matching rule wins, ahead of gestures and mappings. Rules don't run over OmniControl's own window.";
        Rules.CollectionChanged += (_, _) => Summary = CountText(Rules.Count);
    }

    private static string CountText(int count) => count == 1 ? "1 rule" : $"{count} rules";
}
