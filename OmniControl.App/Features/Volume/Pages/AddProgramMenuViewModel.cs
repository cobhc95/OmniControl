using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// The Outputs header's "Add program" pop-down: a search box over the programs OmniControl can offer (recently used and installed,
/// <see cref="ProgramListing.Choices"/>), and a way to browse for an .exe. Choosing one hands its path to the page, which adds the row.
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

    private List<ProgramChoice> _choices = new();

    [ObservableProperty] private string _query = string.Empty;

    /// The menu's rows (<c>Controls/MenuRowList</c>): each matching program with where it comes from, else a note pointing at Browse.
    [ObservableProperty] private IReadOnlyList<MenuRowItem> _rows = Array.Empty<MenuRowItem>();

    /// The first match (Enter in the search box chooses it); null when nothing matches.
    public ProgramChoice? First => _choices.FirstOrDefault();

    partial void OnQueryChanged(string value) => Refresh();

    /// The pop-down opened: start from an empty search and ask for a fresh look at the installed programs.
    public void Opened()
    {
        _open = true;
        Query = string.Empty;
        Refresh();
        _discovery.Refresh();
    }

    public void Closed() => _open = false;

    /// Adds the program at <paramref name="path"/> (a menu entry, or the file the user browsed to).
    public void Add(string path) => _add(path);

    private void OnDiscovered()
    {
        if (_open) Refresh();
    }

    private void Refresh()
    {
        var wanted = ProgramListing.Choices(_mixer.Settings.Apps, _discovery.Installed, Query.Trim());
        if (Rows.Count > 0 && wanted.SequenceEqual(_choices)) return;
        _choices = wanted;
        Rows = wanted.Count == 0
            ? new[] { MenuRowItem.Header("No match. Browse for a program…") }
            : wanted.Select(c => new MenuRowItem(c.Name, string.Empty, false, c.Path, new RelayCommand(() => _add(c.Path)), Trailing: c.Source)).ToList();
    }

    public void Dispose() => _discovery.Changed -= OnDiscovered;
}
