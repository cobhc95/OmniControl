using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Outputs header's "Add program" pop-down: a search box over the programs OmniControl can offer (recently used and installed,
/// <see cref="ProgramListing.Choices"/>), and a way to browse for an .exe. Choosing one hands its path to the page, which adds the row.
/// </summary>
public sealed partial class AddProgramMenuViewModel : ObservableObject, IDisposable
{
    private readonly MixerService _mixer;
    private readonly ProgramDiscovery _discovery;
    private readonly Action<string> _add;
    private bool _open;

    public AddProgramMenuViewModel(MixerService mixer, ProgramDiscovery discovery, Action<string> add)
    {
        _mixer = mixer;
        _discovery = discovery;
        _add = add;
        discovery.Changed += OnDiscovered;
    }

    public ObservableCollection<ProgramChoice> Choices { get; } = new();

    [ObservableProperty] private string _query = string.Empty;

    /// <summary>Nothing matches the search: the menu says so and points at Browse.</summary>
    public bool HasNoMatch => Choices.Count == 0;

    partial void OnQueryChanged(string value) => Refresh();

    /// <summary>The pop-down opened: start from an empty search and ask for a fresh look at the installed programs.</summary>
    public void Opened()
    {
        _open = true;
        Query = string.Empty;
        Refresh();
        _discovery.Refresh();
    }

    public void Closed() => _open = false;

    /// <summary>Adds the program at <paramref name="path"/> (a menu entry, or the file the user browsed to).</summary>
    public void Add(string path) => _add(path);

    private void OnDiscovered()
    {
        if (_open) Refresh();
    }

    private void Refresh()
    {
        var wanted = ProgramListing.Choices(_mixer.Settings.Apps, _discovery.Installed, Query.Trim());
        if (!wanted.SequenceEqual(Choices))
        {
            Choices.Clear();
            foreach (var c in wanted) Choices.Add(c);
        }
        OnPropertyChanged(nameof(HasNoMatch));
    }

    public void Dispose() => _discovery.Changed -= OnDiscovered;
}
