using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Wiring tab's "Virtual cables" group: one <see cref="CableRow"/> per virtual device found, the dashed "+ This device is a
/// cable..." with its pop-down (<see cref="Mark"/>), and under the list the hint with "Get more cables" and, while no cable is
/// installed, Install VB-CABLE. Collapsible like the other Wiring groups.
/// </summary>
public sealed partial class CableGroup : CardGroup
{
    public const string Hint = "More cables let more programs run on cables.";

    public CableGroup(CableMarkMenu mark, ModeCardAction moreCables)
        : base("Volume/Wiring/Cables", "Virtual cables", string.Empty)
    {
        Mark = mark;
        MoreCables = moreCables;
    }

    public ObservableCollection<CableRow> Items { get; } = new();

    public CableMarkMenu Mark { get; }

    /// <summary>"Get more cables": the cable maker's page.</summary>
    public ModeCardAction MoreCables { get; }

    /// <summary>Install VB-CABLE while no cable pair is installed; null = one is.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasInstall))] private ModeCardAction? _install;
    public bool HasInstall => Install is not null;
}
