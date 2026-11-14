using System.Collections.ObjectModel;

namespace OmniControl.App.Pages.Gestures;

/// <summary>The Gestures page's "Custom rules" group: one line per rule; its header says how many there are.</summary>
public sealed class RulesGroup : CardGroup
{
    public ObservableCollection<RuleItem> Rules { get; } = new();

    public RulesGroup() : base("Gestures/Rules", "Custom rules", CountText(0))
    {
        Tip = "The first matching rule wins, ahead of gestures and mappings. Rules don't run over OmniControl's own window.";
        Rules.CollectionChanged += (_, _) => Summary = CountText(Rules.Count);
    }

    private static string CountText(int count) => count == 1 ? "1 rule" : $"{count} rules";
}
