using System.Collections.ObjectModel;

namespace OmniControl.App.Pages.Volume;

/// <summary>A collapsible group of Volume rows (Outputs or Inputs, on either tab); the rows come and go as programs and devices do.</summary>
public sealed class VolumeGroup : CardGroup
{
    public ObservableCollection<object> Items { get; } = new();

    /// <summary>A Wiring table's middle column title ("Plays on", "Records from"); empty for the Mixer's groups.</summary>
    public string RouteColumn { get; init; } = string.Empty;

    /// <summary>A Wiring table's action at the right of its header ("+ Combine microphones"); null = none.</summary>
    public ModeCardAction? Tool { get; init; }
    public bool HasTool => Tool is not null;

    private ModeGate? _toolGate;

    /// <summary>
    /// Whether <see cref="Tool"/> can work now (dimmed with the reason as its tooltip, and a Switch link where another mode allows it); the
    /// page keeps it up to date. Null = the action always works.
    /// </summary>
    public ModeGate? ToolGate
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
