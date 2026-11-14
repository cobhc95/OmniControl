using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The help card's table (the Sound mode panel's Help and the setup badge; built to <c>docs/mockups/help-card-table.html</c>): one
/// group header per <see cref="HelpGroup"/> and one row per <see cref="SoundModes.HelpCombinations"/> entry, each with its live
/// "Works" tag, the current setup highlighted. Clicking a row that works switches to that setup through <c>choose</c>; a row that
/// can't work is greyed and its tag and tooltip say why. The page calls <see cref="Sync"/> whenever the engine's facts change.
/// </summary>
public sealed class SetupTable
{
    public SetupTable(Action<int> choose)
    {
        var items = new List<object>();
        HelpGroup? group = null;
        foreach (var combination in SoundModes.HelpCombinations)
        {
            if (combination.Group != group) items.Add(new SetupGroupRow(group = combination.Group));
            items.Add(new SetupRow(combination, choose));
        }
        Items = items;
    }

    /// <summary>The table's rows in order: <see cref="SetupGroupRow"/>s and <see cref="SetupRow"/>s.</summary>
    public IReadOnlyList<object> Items { get; }

    /// <summary>Each row's "Works" from the live facts, and the current setup (<see cref="SoundModes.SetupNumber"/>).</summary>
    public void Sync(HelpFacts facts)
    {
        var current = SoundModes.SetupNumber(facts);
        foreach (var row in Items.OfType<SetupRow>()) row.Sync(SoundModes.WorksOf(row.Combination, facts), row.Number == current);
    }
}

/// <summary>A group header of the table: its title and the plain note under it.</summary>
public sealed class SetupGroupRow(HelpGroup group)
{
    public string Title => group.Title;
    public string? Note => group.Note;
    public bool HasNote => Note is not null;
}

/// <summary>One setup of the table: its words, whether it works here now (the tag), whether it is the setup now (highlighted).</summary>
public sealed partial class SetupRow : ObservableObject
{
    private readonly Action<int> _choose;

    public SetupRow(HelpCombination combination, Action<int> choose)
    {
        Combination = combination;
        _choose = choose;
    }

    public HelpCombination Combination { get; }
    public int Number => Combination.Number;
    public string MasterRunsOn => Combination.MasterRunsOn;
    public string CableUsedFor => Combination.CableUsedFor;
    public string Programs => Combination.Programs;
    public string MasterEffects => Combination.MasterEffects;

    /// <summary>The master's effects are limited here (Equalizer APO only): the cell is the caution colour, as in the mock-up.</summary>
    public bool MasterLimited => Combination.MasterLimited;

    [ObservableProperty] private string _works = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Tip))] private bool _isAvailable;

    /// <summary>The row can be clicked: it works, or it is Fast mode's (always allowed; its tag says what it lacks).</summary>
    [ObservableProperty] private bool _canChoose;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Tip))] private bool _isCurrent;

    /// <summary>The row's tooltip: where it stands, and what a click does.</summary>
    public string Tip => IsCurrent ? $"Setup {Number}: you are here."
        : !CanChoose ? $"Setup {Number}: {Works}."
        : IsAvailable ? $"Switch to setup {Number}." : $"Switch to setup {Number}. {Works}.";

    public void Sync(HelpWorks works, bool current)
    {
        Works = works.Text;
        IsAvailable = works.Available;
        CanChoose = works.CanChoose;
        IsCurrent = current;
        OnPropertyChanged(nameof(Tip));
    }

    [RelayCommand] private void Choose()
    {
        if (CanChoose && !IsCurrent) _choose(Number);
    }
}
