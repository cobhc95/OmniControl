using Avalonia.Media.Imaging;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Resources.Pages;

/// One App traffic control row: its pin, the program (icon, friendly name, the technical name muted when it differs), its traffic, its upload and
/// download limit chips and its On switch (off blocks it), through <see cref="NetworkLimits"/>.
public sealed partial class AppNetItem : ObservableObject
{
    private const string NoLimit = "None";
    private readonly NetworkLimits _limits;
    private readonly ProcessIcons _icons;
    private readonly Action<string> _report;

    /// <param name="togglePin">Pins or unpins the row (the page keeps the pins).</param>
    internal AppNetItem(string id, NetworkLimits limits, ProcessIcons icons, Action<AppNetItem> togglePin, Action<string> report)
    {
        (Id, _limits, _icons, _report) = (id, limits, icons, report);
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

    public string NameTip => Path.Length > 0 ? Path : Id;

    public string PinTip => IsPinned ? "Unpin" : "Pin to top";

    public IRelayCommand TogglePinCommand { get; }

    /// What download limits really do now (the chip's tooltip).
    public string DownloadTip => _limits.Download.Tip;

    /// False while OmniControl is still looking for the driver or the packet filter is switched off (the chip is greyed out; a saved limit is kept).
    public bool DownloadUsable => _limits.Download.Usable;

    private bool _loading;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Technical));

    /// Shows the program's rule as saved now.
    public void Load()
    {
        var rule = _limits.Rule(Id);
        _loading = true;
        (UploadSet, Upload) = rule is { UploadOn: true, UploadKbps: > 0 } ? (true, Words(rule.UploadKbps)) : (false, NoLimit);
        (DownloadSet, Download) = rule is { DownloadOn: true, DownloadKbps: > 0 } ? (true, Words(rule.DownloadKbps)) : (false, NoLimit);
        IsOn = rule?.Blocked != true;
        _loading = false;
        OnPropertyChanged(nameof(DownloadTip));
        OnPropertyChanged(nameof(DownloadUsable));
    }

    /// The executable became known: its friendly name and icon are read once, off the UI thread.
    public void KnowPath(string path)
    {
        if (Path.Length > 0 || path.Length == 0) return;
        Path = path;
        OnPropertyChanged(nameof(NameTip));
        _ = Guard.RunAsync("App traffic control program name", async () =>
        {
            Name = await Task.Run(() => ProgramFile.NameOf(path));
            Picture = await _icons.GetAsync(path);
        });
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_loading) _limits.Set(Id, r => r.Blocked = !value);
    }

    public IReadOnlyList<MenuRowItem> UploadRows() => Rows(r => r.UploadOn ? r.UploadKbps : 0, (r, kbps) => (r.UploadOn, r.UploadKbps) = (kbps > 0, kbps > 0 ? kbps : r.UploadKbps));

    public IReadOnlyList<MenuRowItem> DownloadRows() => Rows(r => r.DownloadOn ? r.DownloadKbps : 0, (r, kbps) => (r.DownloadOn, r.DownloadKbps) = (kbps > 0, kbps > 0 ? kbps : r.DownloadKbps));

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

    /// "None" then every offered limit, the one in force ticked.
    private IReadOnlyList<MenuRowItem> Rows(Func<AppNetRule, int> now, Action<AppNetRule, int> set)
    {
        var current = _limits.Rule(Id) is { } rule ? now(rule) : 0;
        return NetworkLimits.Choices.Prepend(0).Select(kbps => new MenuRowItem(kbps == 0 ? NoLimit : Words(kbps), string.Empty, kbps == current, string.Empty,
            new RelayCommand(() => _limits.Set(Id, r => set(r, kbps))))).ToList();
    }

    private static string Words(int kbps) => Reading.SpeedWords(kbps * 1024d);
}
