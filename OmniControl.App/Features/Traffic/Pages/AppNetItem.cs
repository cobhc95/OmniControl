using OmniControl.App.Features.Resources;
using Avalonia.Media.Imaging;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Traffic.Pages;

/// One Network Traffic Control row: its pin, the program (icon, friendly name, the technical name muted when it differs), its traffic, its upload and
/// download limit chips and its On switch (off blocks it), through <see cref="NetworkLimits"/>, at the place the page shows (<see cref="NetScope"/>).
public sealed partial class AppNetItem : ObservableObject, IEditorItem
{
    internal const string NoLimit = "No limit", BlockWord = "Block";

    /// Every limit a chip offers, in words (the menu's rows).
    internal static IReadOnlyList<string> LimitWords { get; } = NetworkLimits.Choices.Select(Words).Prepend(NoLimit).Append(BlockWord).ToList();
    private bool _uploadBlocked, _downloadBlocked;
    private readonly INetworkLimits _limits;
    private readonly Func<NetScope> _scope;
    private readonly ProcessIcons _icons;
    private readonly Action<string> _report;

    /// <param name="scope">The place whose rules the page shows.</param>
    /// <param name="togglePin">Pins or unpins the row (the page keeps the pins).</param>
    internal AppNetItem(string id, INetworkLimits limits, Func<NetScope> scope, ProcessIcons icons, Action<AppNetItem> togglePin, Action<string> report)
    {
        (Id, _limits, _scope, _icons, _report) = (id, limits, scope, icons, report);
        _name = id;
        TogglePinCommand = new RelayCommand(() => togglePin(this));
        Load();
    }

    /// The program's identity (<see cref="ProgramIdentity.Normalize"/>).
    public string Id { get; }

    [ObservableProperty] private string _down = Reading.Unknown;
    [ObservableProperty] private string _up = Reading.Unknown;
    [ObservableProperty] private string _upload = NoLimit;
    [ObservableProperty] private bool _uploadSet;
    [ObservableProperty] private string _download = NoLimit;
    [ObservableProperty] private bool _downloadSet;
    [ObservableProperty] private bool _isOn = true;
    [ObservableProperty] private string _name;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPicture))] private Bitmap? _picture;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PinTip))] private bool _isPinned;
    [ObservableProperty] private bool _notRunning;

    /// The executable's path, empty while not known.
    public string Path { get; private set; } = string.Empty;

    public bool HasPicture => Picture is not null;

    /// The technical name beside the friendly one, empty when they are the same.
    public string Technical => string.Equals(Name, Id, StringComparison.OrdinalIgnoreCase) ? string.Empty : Id;

    public string Label => Name;

    /// What the row says now besides its name: both limits and the program's own name.
    public IEnumerable<string> SearchTexts => new[] { Upload, Download, Technical, UploadTip, DownloadTip }.Where(t => t.Length > 0);

    public string NameTip => Path.Length > 0 ? Path : Id;

    public string PinTip => IsPinned ? "Unpin" : "Pin to top";

    public IRelayCommand TogglePinCommand { get; }

    /// The upload chip's tooltip: what its block does while it reads Block, else what a limit is.
    public string UploadTip => _uploadBlocked ? NetworkLimits.UploadBlockTip : "Upload limit (a Windows throttle rule)";

    /// What download limits really do now (the chip's tooltip), while the chip reads Block what a block does, until the driver cannot run it.
    public string DownloadTip => _downloadBlocked && _limits.Download.Usable ? NetworkLimits.DownloadBlockTip : _limits.Download.Tip;

    /// False while OmniControl is still looking for the driver or the packet filter is switched off (the chip is greyed out; a saved limit is kept).
    public bool DownloadUsable => _limits.Download.Usable;

    private bool _loading;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Technical));

    /// Each direction's value as last shown (<see cref="LimitMenu.Now"/>), read by the All apps row.
    public int UploadValue { get; private set; }
    public int DownloadValue { get; private set; }

    /// The program's rule at the place shown, or null.
    public AppNetRule? Rule => _limits.Rule(_scope(), Id);

    /// Shows the program's rule as saved now.
    public void Load()
    {
        var rule = Rule;
        _loading = true;
        (UploadValue, DownloadValue) = (LimitMenu.Upload.Now(rule), LimitMenu.Download.Now(rule));
        (UploadSet, Upload) = (UploadValue != LimitMenu.None, LimitMenu.Words(UploadValue));
        (DownloadSet, Download) = (DownloadValue != LimitMenu.None, LimitMenu.Words(DownloadValue));
        (_uploadBlocked, _downloadBlocked) = (UploadValue == LimitMenu.Blocked, DownloadValue == LimitMenu.Blocked);
        IsOn = rule?.Blocked != true;
        _loading = false;
        OnPropertyChanged(nameof(UploadTip));
        OnPropertyChanged(nameof(DownloadTip));
        OnPropertyChanged(nameof(DownloadUsable));
    }

    /// The executable became known: its friendly name and icon are read once, off the UI thread.
    public void KnowPath(string path)
    {
        if (Path.Length > 0 || path.Length == 0) return;
        Path = path;
        OnPropertyChanged(nameof(NameTip));
        _ = Guard.RunAsync($"{TrafficPageViewModel.PageTitle} program name", async () =>
        {
            Name = await Task.Run(() => ProgramFile.NameOf(path));
            Picture = await _icons.GetAsync(path);
        });
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_loading) Change(r => r.Blocked = !value);
    }

    private void Change(Action<AppNetRule> change) => _limits.Set(_scope(), new[] { Id }, change);

    public IReadOnlyList<MenuRowItem> UploadRows() => LimitMenu.Upload.Rows(LimitMenu.Upload.Now(Rule), Change);

    public IReadOnlyList<MenuRowItem> DownloadRows() => LimitMenu.Download.Rows(LimitMenu.Download.Now(Rule), Change);

    /// The row's right-click menu: pin or unpin, then, when the program's file is known, its folder and its path.
    public IReadOnlyList<MenuRowItem> MenuRows()
    {
        var rows = new List<MenuRowItem> { new(PinTip, string.Empty, false, string.Empty, TogglePinCommand, Icon: "pinned") };
        if (Path.Length == 0) return rows;
        var path = Path;
        rows.Add(new("Open file location", string.Empty, false, path, new RelayCommand(() => OnWorker("Open file location", () => FileReveal.Show(path))), Icon: "folder-open"));
        rows.Add(new("Copy program path", string.Empty, false, path, new RelayCommand(() =>
            OnWorker("Copy program path", () => ImageClipboard.SetText(path) ? null : "The path was not copied: another program is holding the clipboard.")), Icon: "copy"));
        return rows;
    }

    /// Runs a Windows call on a worker and reports its one-sentence failure.
    private void OnWorker(string what, Func<string?> step) => _ = Guard.RunAsync(what, async () =>
    {
        if (await Task.Run(step) is { } problem) _report(problem);
    });

    internal static string Words(int kbps) => Reading.SpeedWords(kbps * 1024d);
}
