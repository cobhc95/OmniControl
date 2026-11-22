namespace OmniControl.App.Features.Volume.Pages;

/// The Wiring tab's "Virtual cables" group: one <see cref="CableRow"/> per virtual device found, the dashed "+ This device is a
/// cable..." with its pop-down (<see cref="Mark"/>), and under the list the hint with "Get more cables" and, while no cable is
/// installed, Install VB-CABLE. Collapsible like the other Wiring groups.
public sealed partial class CableGroup : CardGroup
{
    public const string Hint = "More cables let more programs run on cables.";

    public const string GroupName = "Virtual cables";

    public CableGroup(CableMarkMenu mark, CardAction moreCables)
        : base("Volume/Wiring/Cables", GroupName, string.Empty)
    {
        Mark = mark;
        MoreCables = moreCables;
    }

    public ObservableCollection<CableRow> Items { get; } = new();

    public CableMarkMenu Mark { get; }

    /// "Get more cables": the cable maker's page.
    public CardAction MoreCables { get; }

    /// Install VB-CABLE while no cable pair is installed; null = one is.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasInstall))] private CardAction? _install;
    public bool HasInstall => Install is not null;
}
