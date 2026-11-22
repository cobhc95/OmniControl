using OmniControl.App.Features.Resources;

namespace OmniControl.App.Features.Traffic.Pages;

/// The "All apps" row above the programs: the whole PC's traffic, and limit chips and an On switch that stand for every program row. It stores
/// nothing: what it shows is worked out from the rows (<see cref="Follow"/>); a pick sets that value on every row, and rows that differ read
/// "Mixed" (the switch half on, which a click turns all on).
public sealed partial class AllAppsItem : ObservableObject
{
    public const string Name = "All apps", Detail = "entire PC", MixedWord = "Mixed";

    private readonly INetworkLimits _limits;
    private readonly Func<NetScope> _scope;
    private IReadOnlyList<AppNetItem> _rows = Array.Empty<AppNetItem>();
    private int? _uploadValue, _downloadValue; // null: the rows differ

    internal AllAppsItem(INetworkLimits limits, Func<NetScope> scope)
    {
        (_limits, _scope) = (limits, scope);
        ToggleCommand = new RelayCommand(Toggle);
    }

    [ObservableProperty] private string _down = Reading.Unknown;
    [ObservableProperty] private string _up = Reading.Unknown;
    [ObservableProperty] private string _upload = AppNetItem.NoLimit;
    [ObservableProperty] private bool _uploadSet;
    [ObservableProperty] private bool _uploadMixed;
    [ObservableProperty] private string _download = AppNetItem.NoLimit;
    [ObservableProperty] private bool _downloadSet;
    [ObservableProperty] private bool _downloadMixed;

    /// On when every row is, off when every row is off, null (half) when they differ.
    [ObservableProperty] private bool? _isOn = true;

    public IRelayCommand ToggleCommand { get; }

    public bool DownloadUsable => _limits.Download.Usable;
    public string DownloadTip => _limits.Download.Tip;

    /// Shows what the rows say now and the whole PC's traffic (bytes per second of every program).
    public void Follow(IReadOnlyList<AppNetItem> rows, double received, double sent, bool reading)
    {
        _rows = rows;
        (Down, Up) = reading ? (Reading.SpeedWords(received), Reading.SpeedWords(sent)) : (Reading.Unknown, Reading.Unknown);
        _uploadValue = Same(rows.Select(r => r.UploadValue));
        _downloadValue = Same(rows.Select(r => r.DownloadValue));
        (UploadMixed, UploadSet, Upload) = (_uploadValue is null, _uploadValue is not (null or LimitMenu.None), _uploadValue is { } u ? LimitMenu.Words(u) : MixedWord);
        (DownloadMixed, DownloadSet, Download) = (_downloadValue is null, _downloadValue is not (null or LimitMenu.None), _downloadValue is { } d ? LimitMenu.Words(d) : MixedWord);
        IsOn = rows.All(r => r.IsOn) ? true : rows.All(r => !r.IsOn) ? false : null;
        OnPropertyChanged(nameof(DownloadUsable));
        OnPropertyChanged(nameof(DownloadTip));
    }

    private static int? Same(IEnumerable<int> values)
    {
        var distinct = values.Distinct().Take(2).ToList();
        return distinct.Count switch { 0 => LimitMenu.None, 1 => distinct[0], _ => null };
    }

    public IReadOnlyList<MenuRowItem> UploadRows() => LimitMenu.Upload.Rows(_uploadValue, Change);

    public IReadOnlyList<MenuRowItem> DownloadRows() => LimitMenu.Download.Rows(_downloadValue, Change);

    /// All on turns every row off; anything else (all off, or half) turns every row on.
    private void Toggle()
    {
        var off = IsOn == true;
        Change(r => r.Blocked = off);
    }

    private void Change(Action<AppNetRule> change) => _limits.Set(_scope(), _rows.Select(r => r.Id), change);
}
