using System.Collections.Concurrent;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.OtherPcs.SettingsImport;
using OmniControl.App.Features.States;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.OtherPcs;

/// Other PCs: the paired PCs and everything that passes between this PC and them (LAN method; the same link also works to an address typed
/// for a PC on Tailscale, ZeroTier or any network that carries it). This class owns the saved pairs and what is shared with each, and ties
/// the parts together: <see cref="PcDiscovery"/> finds PCs, <see cref="PcPairing"/> pairs, <see cref="PcSession"/> and <see cref="PcLink"/>
/// are the encrypted link, the share part sends what this PC shares and the remote part registers what the other PCs share in the master
/// lists (<see cref="StateRegistry"/>, <see cref="HotkeyRegistry"/>) as "PC: item" and runs what they ask for through
/// <see cref="HotkeyRegistry.Run(string)"/> (the one run path: gate, chain). Nothing listens or announces until a PC is paired or "Pair a PC"
/// is opened, so Windows' firewall question comes only when the feature is used. Everything here runs on the UI thread; the network
/// tasks hand their results over with <see cref="PcWork.Post"/>.
public sealed partial class OtherPcsService : IPairedPcs, IPcBook, IDisposable
{
    /// The page's title, the category of the actions and the group of the states of other PCs.
    public const string Category = "Other PCs";

    private static readonly DataStore Store = DataStores.OtherPcs;

    private readonly IActionRegistry _hotkeys;
    private readonly IActionRunner _run;
    private readonly IStateRegistry _states;
    private readonly ICredentialStore _credentials;
    private readonly Action<string> _report;
    private readonly Func<bool, string> _shareDevices;
    private readonly SettingsShare _settingsShare;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly OtherPcsSettings _settings;
    private readonly string _ownName = PcIds.CleanName(Environment.MachineName);
    private readonly ConcurrentDictionary<string, byte[]> _secrets = new(); // read by the network tasks; written on the UI thread
    private readonly CancellationTokenSource _stop = new();
    private readonly PcRequests _requests = new();
    private bool _disposed;

    /// <param name="shareDevices">Makes the text of this PC's device set-ups for a paired PC that asks (true = with the user's script templates).</param>
    /// <param name="settingsShare">Hands the settings areas to a paired PC that imports them.</param>
    /// <param name="ask">Asks the person at this PC a yes/no question (a pairing to confirm, a paired PC asking to change a wake setting or to put this PC to sleep).</param>
    public OtherPcsService(IActionRegistry hotkeys, IActionRunner run, IStateRegistry states, ICredentialStore credentials, Action<string> report, Func<bool, string> shareDevices, Func<DialogMessage, Task<bool>> ask, SettingsShare settingsShare)
    {
        _shareDevices = shareDevices;
        _settingsShare = settingsShare;
        _ask = ask;
        _hotkeys = hotkeys;
        _run = run;
        _states = states;
        _credentials = credentials;
        _report = report;
        _settings = Load();
        _attempts = new PairAttempts(_stop.Token);
        foreach (var pair in _settings.Pairs)
        {
            if (ReadSecret(pair.Id) is { } secret) _secrets[pair.Id] = secret;
        }
        Wake = new PcWake(this, _requests, hotkeys, report, ask, _ownName, new WakeGate());
        Tailscale = new TailscaleLink(report, RaiseChanged, _stop.Token);
        WatchShares();
        RestoreCatalogs();
        foreach (var pair in _settings.Pairs) Wake.Register(pair);
        if (_settings.Pairs.Count > 0) StartNetwork();
    }

    /// Waking the paired PCs, and this PC's own wake set-up.
    public PcWake Wake { get; }

    /// Tailscale's state and fixes for pairing over the internet.
    public TailscaleLink Tailscale { get; }

    /// This PC's own name, as other PCs see it.
    public string OwnName => _ownName;

    private string OwnId => _settings.OwnId;

    /// The paired PCs, in the order they were paired.
    public IReadOnlyList<PairedPc> Pairs => _settings.Pairs.ToList();

    public PairedPc? Find(string id) => _settings.Pairs.FirstOrDefault(p => p.Id == id);

    /// Raised when pairs, who is online, who is nearby or the pairing panel's state changed (UI thread).
    public event Action? Changed;

    private void RaiseChanged() => Changed?.Invoke();

    PcLink? IPcBook.LinkOf(string id) => _links.GetValueOrDefault(id);

    IReadOnlyList<PcLink> IPcBook.Links => _links.Values.ToList();

    void IPcBook.Save() => Save();

    void IPcBook.RaiseChanged() => RaiseChanged();

    /// True when the paired PC is connected now (it proved the key and the link is alive).
    public bool IsOnline(string id) => _links.ContainsKey(id);

    /// Whether the pairing key of a pair is held (a pair imported from a backup, or whose key Windows lost, must be unpaired and paired again).
    public bool HasKey(string id) => _secrets.ContainsKey(id);

    /// What an offline PC's tag says: when it was last connected, or just "offline" when it never was.
    public string SeenWords(string id) => Find(id)?.LastSeen is not { } seen ? "offline"
        : seen.Date == DateTime.Today ? $"last seen {seen:HH:mm}" : $"last seen {seen:d MMM}";

    private static string CredentialTarget(string id) => $"OmniControl Other PC {id}";

    private byte[]? ReadSecret(string id)
    {
        try
        {
            var secret = Convert.FromBase64String(_credentials.Read(CredentialTarget(id)) ?? string.Empty);
            return secret.Length == PcProtocol.KeyBytes ? secret : null;
        }
        catch (FormatException)
        {
            return null; // not a key this feature wrote: the pair has to be made again
        }
    }

    private OtherPcsSettings Load()
    {
        var loaded = AppDataStore.Load<OtherPcsSettings>(Store) ?? new OtherPcsSettings();
        var clean = new OtherPcsSettings { OwnId = PcIds.IsValid(loaded.OwnId) ? loaded.OwnId : PcIds.New() };
        foreach (var pair in loaded.Pairs ?? new List<PairedPc>())
        {
            if (!PcIds.IsValid(pair.Id) || pair.Id == clean.OwnId || clean.Pairs.Any(p => p.Id == pair.Id)) continue;
            pair.Name = PcIds.CleanName(pair.Name);
            pair.Nickname = string.IsNullOrWhiteSpace(pair.Nickname) ? string.Empty : PcIds.CleanName(pair.Nickname);
            pair.Address ??= string.Empty;
            pair.TailscaleAddress ??= string.Empty;
            if (pair.Route == PcRoute.Tailscale && pair.TailscaleAddress.Length == 0) pair.TailscaleAddress = pair.Address; // saved before the Tailscale address had its own place
            clean.Pairs.Add(pair);
        }
        foreach (var tab in loaded.SetupTabs ?? new List<SetupTab>())
        {
            if (string.IsNullOrWhiteSpace(tab.Id) || clean.SetupTabs.Any(t => t.Id == tab.Id)) continue;
            tab.Address ??= string.Empty;
            tab.Target ??= string.Empty;
            clean.SetupTabs.Add(tab);
        }
        var known = clean.Pairs.Select(p => p.Id).Concat(clean.SetupTabs.Select(t => t.Id)).ToList();
        clean.TabOrder = (loaded.TabOrder ?? new List<string>()).Where(known.Contains).Concat(known).Distinct().ToList(); // saved order first, whatever it lacks after
        if (clean.OwnId != loaded.OwnId) AppDataStore.Save(Store, clean);
        return clean;
    }

    private void Save() => AppDataStore.SaveSoon(Store, _settings);


    /// Takes a PC out: its link ends, what it shared leaves the master lists, its key is forgotten. The other PC still lists this one until it unpairs too.
    public void Unpair(string id)
    {
        if (Find(id) is not { } pair) return;
        CloseLink(id);
        ForgetRemote(id);
        ClearCountdown(id);
        _credentials.Delete(CredentialTarget(id));
        _secrets.TryRemove(id, out _);
        Wake.Unregister(id);
        _settings.Pairs.Remove(pair);
        _settings.TabOrder.Remove(id);
        TakePairedIds();
        Save();
        StopNetworkIfIdle();
        RaiseChanged();
    }

    /// The ids of all tabs, paired PCs and New PC tabs, in the saved order.
    public IReadOnlyList<string> TabOrder => _settings.TabOrder.ToList();

    /// Moves a tab (a paired PC or a New PC tab) to another place (a tab dragged); the one order is saved.
    public void Move(string id, int to)
    {
        var from = _settings.TabOrder.IndexOf(id);
        if (from < 0 || to < 0 || to >= _settings.TabOrder.Count || to == from) return;
        _settings.TabOrder = Reorder.Moved(_settings.TabOrder, new[] { from }, to);
        Save();
        RaiseChanged();
    }

    /// The display a per-display card of a paired PC drives (empty: all displays).
    public string ScreenPick(string id, string card) => Find(id)?.ScreenPicks.GetValueOrDefault(card) ?? string.Empty;

    public void SetScreenPick(string id, string card, string display)
    {
        if (Find(id) is not { } pair) return;
        if (display.Length == 0) pair.ScreenPicks.Remove(card); else pair.ScreenPicks[card] = display;
        Save();
    }

    /// Gives a paired PC a nickname that is kept on this PC only (empty removes it). Everything it shared is listed again under the new name at once.
    public void Rename(string id, string nickname)
    {
        if (Find(id) is not { } pair) return;
        pair.Nickname = string.IsNullOrWhiteSpace(nickname) ? string.Empty : PcIds.CleanName(nickname);
        Save();
        Reregister(id);
        RaiseChanged();
    }

    /// Whether a paired PC may do <paramref name="what"/> on this PC (its menu's Allowed here).
    public bool Allows(string id, PcAllowance what) => Find(id) is { } pair && what switch
    {
        PcAllowance.Power => pair.MayRunPower,
        PcAllowance.Actions => pair.MayRunActions,
        _ => pair.MayReadSettings,
    };

    public void SetAllows(string id, PcAllowance what, bool on)
    {
        if (Find(id) is not { } pair) return;
        switch (what)
        {
            case PcAllowance.Power: pair.MayRunPower = on; break;
            case PcAllowance.Actions: pair.MayRunActions = on; break;
            default: pair.MayReadSettings = on; break;
        }
        Save();
        RaiseChanged();
    }

    /// Chooses how a paired PC is reached: found on the network, at the address typed for it, or over Tailscale. The typed address is kept whichever is chosen. Choosing Tailscale does not drop the current link: the PC keeps its route until the Tailscale checks of that PC all pass (<see cref="CheckPairTailscale"/>).
    public void SetRoute(string id, PcRoute route, string address)
    {
        if (Find(id) is not { } pair) return;
        if (pair.Route != route && _links.TryGetValue(id, out var link))
        {
            // The other PC hears it over the working link first; only then this PC switches.
            _ = Guard.RunAsync("Other PCs: route change", async () =>
            {
                var sent = await SendRoute(link, route).ConfigureAwait(false);
                PcWork.Post(() => ChooseRoute(id, route, address, sent));
            });
        }
        else ChooseRoute(id, route, address, sent: false);
    }

    private void ChooseRoute(string id, PcRoute route, string address, bool sent)
    {
        if (Find(id) is not { } pair) return;
        var changed = pair.Route != route;
        SwitchRoute(pair, route, "chosen by the owner");
        if (route == PcRoute.Address) pair.Address = address.Trim();
        if (changed)
        {
            pair.PendingRoute = sent ? null : route;
            _routeNotes[id] = sent ? "Switched on both PCs." : $"{pair.DisplayName} will switch when it next connects.";
        }
        Save();
        AfterRouteChange(id, route);
        RaiseChanged();
    }

    /// Writes every change of a PC's route and its cause to the log; the route is only ever changed by the owner's pick or by pairing again.
    private void LogRoute(PairedPc pair, PcRoute to, string cause)
    {
        if (pair.Route != to) AppDataStore.AppendLog(Guard.LogFile, $"Other PCs: route of {pair.Name} changed from {pair.Route} to {to} ({cause}).");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Wake.Dispose();
        _stop.Cancel();
        UnwatchShares();
        _setTimer?.Stop();
        StopNetwork();
        foreach (var link in _links.Values.ToList()) link.Dispose();
        _links.Clear();
        foreach (var pair in _settings.Pairs) ForgetRemote(pair.Id);
        _stop.Dispose();
    }
}
