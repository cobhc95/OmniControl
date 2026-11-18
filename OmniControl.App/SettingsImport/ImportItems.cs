using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.SettingsImport;

/// <summary>One tick box of the choose step: an area the other PC has, its muted note and live count.</summary>
public sealed partial class ImportAreaItem : ObservableObject
{
    private readonly Action _changed;

    public ImportAreaItem(ImportArea area, int? count, Action changed)
    {
        Area = area;
        _changed = changed;
        Detail = string.Join(" · ", new[] { area.Note, count is { } n ? $"{n} {(n == 1 ? "entry" : "entries")}" : null }.Where(t => !string.IsNullOrEmpty(t)));
    }

    public ImportArea Area { get; }

    public string Name => Area.Name;

    /// <summary>The note and the count the other PC has now, muted at the row's right.</summary>
    public string Detail { get; }

    [ObservableProperty] private bool _isChecked = true;

    partial void OnIsCheckedChanged(bool value) => _changed();

    [RelayCommand]
    private void Toggle() => IsChecked = !IsChecked;
}

/// <summary>A group heading and its areas (two columns in the window).</summary>
public sealed record ImportGroupItem(string Heading, IReadOnlyList<ImportAreaItem> Areas);

/// <summary>Where one row of the progress and result list stands.</summary>
public enum ImportRowState
{
    Pending,
    Running,
    Checked,
    Failed,

    /// <summary>The "+ N more checked" row that stands for the checked areas a long result hides.</summary>
    More,
}

/// <summary>One row of the progress and result list: "Backup of this PC" or an area, with its mark, its words and an optional fact.</summary>
public sealed partial class ImportProgressRow : ObservableObject
{
    public ImportProgressRow(string label, ImportRowState state = ImportRowState.Pending, string? text = null)
    {
        Label = label;
        _state = state;
        _text = text ?? string.Empty;
    }

    public string Label { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPending), nameof(IsRunning), nameof(IsChecked), nameof(IsFailed), nameof(IsMore))]
    private ImportRowState _state;

    /// <summary>The right-hand word: "checked", "importing", "not applied, kept yours" or a count.</summary>
    [ObservableProperty] private string _text;

    /// <summary>An owner's fact about the area ("2 need passwords"), a yellow tag; empty: none.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFact))] private string _fact = string.Empty;

    /// <summary>For a failed area: the one sentence why (the tag's tooltip and the details).</summary>
    [ObservableProperty] private string _why = string.Empty;

    public bool HasFact => Fact.Length > 0;

    public bool IsPending => State == ImportRowState.Pending;

    public bool IsRunning => State == ImportRowState.Running;

    public bool IsChecked => State == ImportRowState.Checked;

    public bool IsFailed => State == ImportRowState.Failed;

    public bool IsMore => State == ImportRowState.More;
}
