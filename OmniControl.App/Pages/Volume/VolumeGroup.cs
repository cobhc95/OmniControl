using System.Collections.ObjectModel;

namespace OmniControl.App.Pages.Volume;

/// <summary>A collapsible group of Volume rows (Outputs or Inputs, on either tab); the rows come and go as programs and devices do.</summary>
public sealed class VolumeGroup : CardGroup
{
    public ObservableCollection<object> Items { get; } = new();

    /// <summary>The title of the column that says where the rows play or record from ("Plays on", "Records from"), on both tabs.</summary>
    public string RouteColumn { get; init; } = string.Empty;

    /// <summary>The Mixer group's rows have a pan knob (Outputs), so its column has a heading.</summary>
    public bool ShowsPan { get; init; }

    /// <summary>The group's rows have a Listen button (Microphones), so its column has a heading.</summary>
    public bool ShowsListen { get; init; }

    /// <summary>A Wiring table's action at the right of its header ("+ Combine microphones"); null = none.</summary>
    public CardAction? Tool { get; init; }
    public bool HasTool => Tool is not null;

    private ControlGate? _toolGate;

    /// <summary>
    /// Whether <see cref="Tool"/> can work now (dimmed with the reason as its tooltip); the
    /// page keeps it up to date. Null = the action always works.
    /// </summary>
    public ControlGate? ToolGate
    {
        get => _toolGate;
        init
        {
            _toolGate = value;
            if (value is not null) value.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ToolTipText));
        }
    }

    /// <summary>The action's tooltip: why it is dimmed, else what it does.</summary>
    public string? ToolTipText => ToolGate?.Reason ?? Tool?.Tip;

    /// <summary>The Outputs group's "Show closed" switch and "Add program" menu; null on the other groups.</summary>
    public ProgramListTools? ProgramTools { get; set; }
    public bool HasProgramTools => ProgramTools is not null;

    public VolumeGroup(string key, string name) : base(key, name, string.Empty) { }
}
