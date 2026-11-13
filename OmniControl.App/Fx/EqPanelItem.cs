using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Pages;
using OmniControl.App.Services;

namespace OmniControl.App.Fx;

/// <summary>
/// The Parametric equaliser's editor (laid out after REAPER's ReaEQ): the response graph, one tab per band, the selected band's controls,
/// Add band / Remove band / Reset defaults and the view switches. One card in the FX window's editor list.
/// </summary>
public sealed partial class EqPanelItem : ObservableObject, IEditorItem, ISideNavigable, IEqEditor, IRedrawSource
{
    private bool _loading;

    public EqPanelItem() => Load(Eq.Default());

    public string Label => "Equaliser graph";
    public ObservableCollection<EqBandItem> Bands { get; } = new();
    IReadOnlyList<object> IEqEditor.BandCards => Bands;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Selected))] private int _selectedIndex = -1;
    public EqBandItem? Selected => SelectedIndex >= 0 && SelectedIndex < Bands.Count ? Bands[SelectedIndex] : null;
    public bool HasBands => Bands.Count > 0;

    /// <summary>The equaliser's overall gain (its own card), drawn into the curve.</summary>
    public double OverallGainDb
    {
        get => _overallGainDb;
        set { _overallGainDb = value; Redraw?.Invoke(); }
    }
    private double _overallGainDb;

    /// <summary>A band was changed, added or removed by the user: save it.</summary>
    public event Action? Edited;

    /// <summary>Anything the graph shows changed.</summary>
    public event Action? Redraw;

    /// <summary>Left/Right on the card pick the previous or next band.</summary>
    public void Step(int direction)
    {
        if (HasBands) SelectedIndex = Math.Clamp(SelectedIndex + direction, 0, Bands.Count - 1);
    }

    partial void OnSelectedIndexChanged(int value)
    {
        foreach (var b in Bands) b.IsSelected = b.Index == value;
        Redraw?.Invoke();
    }

    // ---- view switches (shared by every equaliser, kept in settings.json) ----

    public bool ShowTabs { get => AppSettings.Current.EqShowTabs; set => SetView(v => v.EqShowTabs = value); }
    public bool ShowGrid { get => AppSettings.Current.EqShowGrid; set => SetView(v => v.EqShowGrid = value); }
    public bool ShowPhase { get => AppSettings.Current.EqShowPhase; set => SetView(v => v.EqShowPhase = value); }

    private void SetView(Action<AppSettings> set)
    {
        set(AppSettings.Current);
        AppSettings.Current.Save();
        OnPropertyChanged(nameof(ShowTabs));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowPhase));
        Redraw?.Invoke();
    }

    // ---- band list ----

    [RelayCommand(CanExecute = nameof(CanAddBand))]
    private void AddBand()
    {
        var band = Eq.NewBand(Export());
        Bands.Add(Item(Bands.Count, band));
        SelectedIndex = Bands.Count - 1;
        CountChanged();
        ListChanged();
    }

    private bool CanAddBand() => Bands.Count < Eq.MaxBands;

    [RelayCommand(CanExecute = nameof(HasBands))]
    private void RemoveBand()
    {
        if (Selected is not { } band) return;
        var at = SelectedIndex;
        Bands.Remove(band);
        for (var i = at; i < Bands.Count; i++) Bands[i].Index = i;
        SelectedIndex = -1;
        SelectedIndex = Math.Min(at, Bands.Count - 1);
        CountChanged();
        ListChanged();
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        Load(Eq.Default());
        ListChanged();
    }

    private void ListChanged()
    {
        Edited?.Invoke();
        Redraw?.Invoke();
    }

    /// <summary>The band count changed: what depends on it follows.</summary>
    private void CountChanged()
    {
        OnPropertyChanged(nameof(HasBands));
        AddBandCommand.NotifyCanExecuteChanged();
        RemoveBandCommand.NotifyCanExecuteChanged();
    }

    private EqBandItem Item(int index, EqBand band)
    {
        var item = new EqBandItem(index, OnEdited, b => SelectedIndex = b.Index);
        item.Load(band);
        return item;
    }

    private void OnEdited(EqBandItem band)
    {
        if (!_loading) Edited?.Invoke();
        Redraw?.Invoke();
    }

    /// <summary>Shows saved bands without counting as an edit; the selection stays on the same place where it can.</summary>
    public void Load(IReadOnlyList<EqBand> bands)
    {
        _loading = true;
        var keep = SelectedIndex;
        Bands.Clear();
        for (var i = 0; i < bands.Count; i++) Bands.Add(Item(i, bands[i]));
        SelectedIndex = -1;
        SelectedIndex = Bands.Count == 0 ? -1 : Math.Clamp(keep, 0, Bands.Count - 1);
        _loading = false;
        CountChanged();
        Redraw?.Invoke();
    }

    public List<EqBand> Export() => Bands.Select(b => b.ToBand()).ToList();
}
