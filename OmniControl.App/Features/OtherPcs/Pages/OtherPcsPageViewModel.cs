using OmniControl.App.Features.OtherPcs;
using OmniControl.App.Features.OtherPcs.SettingsImport;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// The Other PCs page: the title line, the pairing panel while pairing, the paired PCs
/// as browser-style tabs (the "+" pairs another; a drag reorders them, saved) and under them the chosen one (<see cref="PcItem"/>: its basic set as groups).
/// Everything real is <see cref="OtherPcsService"/>'s; this follows its events and holds what the controls bind to.
public sealed partial class OtherPcsPageViewModel : PageViewModel, IDisposable
{
    private readonly OtherPcsService _service;
    private readonly PcWake _wake;
    private readonly TailscaleLink _tailscale;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Func<DialogMessage, Task<string?>> _askText;
    private readonly Action<string> _report;
    private readonly ImportFromPcService _importFrom;

    public static readonly PageInfo Info = new(OtherPcsService.Category,
        "M3,4 H17 V14 H3 Z M5,6 V12 H15 V6 Z M7,16 H13 V18 H7 Z M19,8 H22 V20 H19 Z M20,9.5 V10.5 H21 V9.5 Z", NavGroups.Devices,
        "other pcs computers pair pairing code lan network tailscale zeroTier remote share shared states actions run another pc laptop");

    public override bool ShowsOwnHeader => true;

    /// The tabs: the paired PCs (<see cref="PcItem"/>) and the unfinished New PC tabs (<see cref="SetupTabItem"/>), in the one saved order.
    public ObservableCollection<object> Items { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelected))] private object? _selected;

    public bool NoTabs => Items.Count == 0;

    public bool HasTabs => Items.Count > 0;

    public bool HasSelected => Selected is not null;

    /// A network here is set to Public in Windows, so other PCs can't find this one on it.
    public bool OnPublicNetwork => _service.OnPublicNetwork;

    [RelayCommand]
    private void OpenNetworkSettings()
    {
        if (WebPage.Open(OtherPcsService.NetworkSettingsPage, _report) is { } problem) _report(problem);
    }

    private const string ThisPcWindowKey = "this-pc"; // cannot clash with a PC's id
    private readonly Dictionary<string, SingleWindow<WakeSetupWindow>> _wakeWindows = new();

    /// This PC's wake tag and cogwheel on the title line.
    public ThisPcWake ThisPc { get; }
    private bool _ready;
    private bool _onScreen;

    public OtherPcsPageViewModel(OtherPcsService service, Func<DialogMessage, Task<bool>> ask, Func<DialogMessage, Task<string?>> askText, Action<string> report, ImportFromPcService importFrom)
        : base(Info)
    {
        _service = service;
        _wake = service.Wake;
        _tailscale = service.Tailscale;
        _ask = ask;
        _askText = askText;
        _report = report;
        _importFrom = importFrom;
        foreach (var id in service.TabOrder)
        {
            if (service.Find(id) is { } pair) Items.Add(Make(pair));
            else if (service.SetupTabs.FirstOrDefault(t => t.Id == id) is { } tab) Items.Add(new SetupTabItem(tab, service, _tailscale, ask));
        }
        Selected = Items.FirstOrDefault();
        _ready = true; // opening a New PC tab makes this PC visible to pairing: only when the person selects one, never at start-up
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(NoTabs));
            OnPropertyChanged(nameof(HasTabs));
        };
        ThisPc = new ThisPcWake(service, _wake);
        ThisPc.SetupRequested += () => OpenWakeSetup(ThisPcWindowKey, () => new OwnWakeTarget(service, _wake));
        service.Changed += OnServiceChanged;
        service.RemoteChanged += OnRemoteChanged;
        service.RemoteValueChanged += OnRemoteValue;
        service.CountdownChanged += OnCountdown;
    }

    private void OnCountdown(string pcId) => Pcs.FirstOrDefault(p => p.Id == pcId)?.RefreshCountdown();

    private PcItem Make(PairedPc pair)
    {
        var item = new PcItem(pair, _service, _wake, _ask, _askText, _report);
        item.SetupWakeRequested += OpenWakeSetup;
        item.ImportRequested += pc => _importFrom.Open(pc.Id);
        return item;
    }

    private void OpenWakeSetup(PcItem pc) => OpenWakeSetup(pc.Id, () => new PairedWakeTarget(pc.Id, _service, _wake));

    /// Opens the wake set-up of a PC, or of this PC (one window per PC; asking again brings it to the front).
    private void OpenWakeSetup(string key, Func<IWakeTarget> target)
    {
        if (!_wakeWindows.TryGetValue(key, out var window)) _wakeWindows[key] = window = new SingleWindow<WakeSetupWindow>(_report);
        window.Show(() => new WakeSetupWindow { DataContext = new WakeSetupViewModel(target()) }, closed => (closed.DataContext as IDisposable)?.Dispose(), "Couldn't open the wake set-up");
    }

    partial void OnSelectedChanged(object? value)
    {
        if (value is PcItem pc) pc.Rebuild();
        else if (value is SetupTabItem tab && _ready) tab.Open();
    }

    private IEnumerable<PcItem> Pcs => Items.OfType<PcItem>();

    private static string TabId(object tab) => tab is PcItem pc ? pc.Id : ((SetupTabItem)tab).Id;

    // The tabs follow the service: PCs paired or unpaired, New PC tabs opened, closed or turned into a PC's tab, and the one saved order; the ones that stay are kept.
    private void OnServiceChanged()
    {
        OnPropertyChanged(nameof(OnPublicNetwork));
        var pairs = _service.Pairs;
        var tabs = _service.SetupTabs;
        var keep = Selected; // moving a tab can drop the list's selection for a moment
        var keepAt = keep is null ? -1 : Items.IndexOf(keep);
        foreach (var gone in Pcs.Where(i => pairs.All(p => p.Id != i.Id)).ToList()) Items.Remove(gone);
        foreach (var gone in Items.OfType<SetupTabItem>().Where(i => tabs.All(t => t.Id != i.Id)).ToList())
        {
            Items.Remove(gone);
            gone.Dispose();
        }
        var added = new List<PcItem>();
        foreach (var pair in pairs.Where(p => Pcs.All(i => i.Id != p.Id)))
        {
            var item = Make(pair);
            added.Add(item);
            Items.Add(item);
        }
        foreach (var tab in tabs.Where(t => Items.OfType<SetupTabItem>().All(i => i.Id != t.Id))) Items.Add(new SetupTabItem(tab, _service, _tailscale, _ask));
        var order = _service.TabOrder;
        for (var i = 0; i < order.Count; i++)
        {
            var at = Items.ToList().FindIndex(t => TabId(t) == order[i]);
            if (at >= 0 && at != i) Items.Move(at, i);
        }
        foreach (var item in Pcs) item.Refresh();
        foreach (var item in Items.OfType<SetupTabItem>()) item.Refresh();
        if (keep is not null && Items.Contains(keep)) Selected = keep;
        else if (added.Count > 0) Selected = added[0]; // a New PC tab that was paired is now the PC's tab
        else Selected = Items.Count == 0 ? null : Items[Math.Clamp(keepAt, 0, Items.Count - 1)];
    }

    /// A tab (a paired PC or a New PC tab) was dragged to another place: the service keeps the one order (the tabs follow its change).
    public void Move(int from, int to)
    {
        if (from >= 0 && from < Items.Count && to >= 0 && to < Items.Count) _service.Move(TabId(Items[from]), to);
    }

    private void OnRemoteChanged() => (Selected as PcItem)?.Rebuild();

    private void OnRemoteValue(StateEntry state) => (Selected as PcItem)?.Update(state);

    /// "+": a new New PC tab, opened.
    [RelayCommand]
    private void NewPc()
    {
        var tab = _service.AddSetupTab();
        Selected = Items.OfType<SetupTabItem>().FirstOrDefault(i => i.Id == tab.Id);
    }

    /// Esc on the open New PC tab closes it (asking first only when something was typed).
    public void CloseSetupTab(SetupTabItem tab) => tab.CloseCommand.Execute(null);

    /// Ctrl+W: closes the open tab: a New PC tab (asks only when unfinished) or a linked PC's tab (asks, then unpairs).
    [RelayCommand]
    private void CloseCurrent()
    {
        if (Selected is SetupTabItem tab) tab.CloseCommand.Execute(null);
        else if (Selected is PcItem pc) pc.UnpairCommand.Execute(null);
    }

    /// The module's "New PC tab" and "Close PC tab" actions (any thread): they work only while this page is on screen, so a key bound to them never acts from another page.
    public void RunTabAction(bool newTab) => Dispatcher.UIThread.Post(() =>
    {
        if (_onScreen) (newTab ? NewPcCommand : CloseCurrentCommand).Execute(null);
    });

    public override void OnOnScreenChanged(bool onScreen) => _onScreen = onScreen;

    public void Dispose()
    {
        ThisPc.Dispose();
        _service.Changed -= OnServiceChanged;
        _service.RemoteChanged -= OnRemoteChanged;
        _service.RemoteValueChanged -= OnRemoteValue;
        _service.CountdownChanged -= OnCountdown;
        foreach (var tab in Items.OfType<SetupTabItem>()) tab.Dispose();
        foreach (var window in _wakeWindows.Values) window.Close();
    }
}
