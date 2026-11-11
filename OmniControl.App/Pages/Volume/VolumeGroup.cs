using System.Collections.ObjectModel;

namespace OmniControl.App.Pages.Volume;

/// <summary>A collapsible group of Volume rows (Outputs or Inputs); the rows come and go as programs and devices do.</summary>
public sealed class VolumeGroup : CardGroup
{
    public ObservableCollection<object> Items { get; } = new();

    public VolumeGroup(string key, string name) : base(key, name, string.Empty) { }
}
