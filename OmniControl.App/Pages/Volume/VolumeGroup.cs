using System.Collections.ObjectModel;

namespace OmniControl.App.Pages.Volume;

/// <summary>A collapsible group of Volume rows (Outputs or Inputs, on either tab); the rows come and go as programs and devices do.</summary>
public sealed class VolumeGroup : CardGroup
{
    public ObservableCollection<object> Items { get; } = new();

    /// <summary>A Wiring table's middle column title ("Plays on", "Records from"); empty for the Mixer's groups.</summary>
    public string RouteColumn { get; init; } = string.Empty;

    public VolumeGroup(string key, string name) : base(key, name, string.Empty) { }
}
