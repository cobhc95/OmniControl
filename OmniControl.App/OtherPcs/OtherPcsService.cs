using System.Collections.Concurrent;
using Avalonia.Threading;
using OmniControl.App.Dialogs;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.App.SettingsImport;
using OmniControl.App.States;
using OmniControl.Core.Engine.Wake;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.OtherPcs;

/// <summary>
/// Other PCs: the paired PCs and everything that passes between this PC and them (LAN method; the same link also works to an address typed
/// for a PC on Tailscale, ZeroTier or any network that carries it). This class owns the saved pairs and what is shared with each, and ties
/// the parts together: <see cref="PcDiscovery"/> finds PCs, <see cref="PcPairing"/> pairs, <see cref="PcSession"/> and <see cref="PcLink"/>
/// are the encrypted link, the share part sends what this PC shares and the remote part registers what the other PCs share in the master
/// lists (<see cref="StateRegistry"/>, <see cref="HotkeyRegistry"/>) as "PC: item" and runs what they ask for through
/// <see cref="HotkeyRegistry.Run(string)"/> (the one run path: gate, chain). Nothing listens or announces until a PC is paired or "Pair a PC"
/// is opened, so Windows' firewall question comes only when the feature is used. Everything here runs on the UI thread; the network
/// tasks hand their results over with <see cref="Post"/>.
/// </summary>
public sealed partial class OtherPcsService : IDisposable
{
    /// <summary>The page's title, the category of the actions and the group of the states of other PCs.</summary>
    public const string Category = "Other PCs";

    private static readonly DataStore Store = DataStores.OtherPcs;

    private readonly HotkeyRegistry _hotkeys;
    private readonly StateRegistry _states;
    private readonly ICredentialStore _credentials;
    private readonly Action<string> _report;
    private readonly Func<bool, string> _shareDevices;
    private readonly SettingsShare _settingsShare;
    private readonly Func<DialogMessage, Task<bool>> _askHere;
    private readonly OtherPcsSettings _settings;
    private readonly string _ownName = PcIds.CleanName(Environment.MachineName);
    private readonly ConcurrentDictionary<string, byte[]> _secrets = new(); // read by the network tasks; written on the UI thread
    private readonly CancellationTokenSource _stop = new();
    private bool _disposed;

    /// <param name="shareDevices">Makes the text of this PC's device set-ups for a paired PC that asks (true = with the user's script templates).</param>
    /// <param name="settingsShare">Hands the settings areas to a paired PC that imports them.</param>
    /// <param name="ask">Asks the person at this PC a yes/no question (a paired PC asking to change a wake setting or to put this PC to sleep).</param>
    public OtherPcsService(HotkeyRegistry hotkeys, StateRegistry states, ICredentialStore credentials, Action<string> report, Func<bool, string> shareDevices, Func<DialogMessage, Task<bool>> ask, SettingsShare settingsShare)
    {
        _askHere = ask;
        _shareDevices = shareDevices;
        _settingsShare = settingsShare;
        _hotkeys = hotkeys;
        _states = states;
        _credentials = credentials;
        _report = report;
        _settings = Load();
        foreach (var pair in _settings.Pairs)
        {
            if (ReadSecret(pair.Id) is { } secret) _secrets[pair.Id] = secret;
        }
        if (StartupMode.Inert) return;
        WakeReader.Failed += LogWakeRead;
        WakeReader.Answered += LogWakeRead;
        WatchShares();
        RestoreCatalogs();
        RegisterWakeActions();
        if (_settings.Pairs.Count > 0) StartNetwork();
    }

    /// <summary>This PC's own name, as other PCs see it.</summary>
    public string OwnName => _ownName;

    private string OwnId => _settings.OwnId;

    /// <summary>The paired PCs, in the order they were paired.</summary>
    public IReadOnlyList<PairedPc> Pairs => _settings.Pairs.ToList();

    public PairedPc? Find(string id) => _settings.Pairs.FirstOrDefault(p => p.Id == id);

    /// <summary>Raised when pairs, who is online, who is nearby or the pairing panel's state changed (UI thread).</summary>
    public event Action? Changed;

    private void RaiseChanged() => Changed?.Invoke();

    /// <summary>True when the paired PC is connected now (it proved the key and the link is alive).</summary>
    public bool IsOnline(string id) => _links.ContainsKey(id);

    /// <summary>Whether the pairing key of a pair is held (a pair imported from a backup, or whose key Windows lost, must be paired again).</summary>
    public bool HasKey(string id) => _secrets.ContainsKey(id);

    /// <summary>What an offline PC's tag says: when it was last connected, or just "offline" when it never was.</summary>
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

    private void Save() => AppDataStore.Save(Store, _settings);

    /// <summary>Runs <paramref name="work"/> on the UI thread; a failure in it is logged and never reaches the caller (a network task).</summary>
    private static void Post(Action work) => Dispatcher.UIThread.Post(() => Guard.Run("Other PCs", work));

    /// <summary>Takes a PC out: its link ends, what it shared leaves the master lists, its key is forgotten. The other PC still lists this one until it unpairs too.</summary>
    public void Unpair(string id)
    {
        if (Find(id) is not { } pair) return;
        CloseLink(id);
        ForgetRemote(id);
        _credentials.Delete(CredentialTarget(id));
        _secrets.TryRemove(id, out _);
        _hotkeys.Unregister(WakeActionId(id));
        _settings.Pairs.Remove(pair);
        _settings.TabOrder.Remove(id);
        Save();
        RaiseChanged();
    }

    /// <summary>The ids of all tabs, paired PCs and New PC tabs, in the saved order.</summary>
    public IReadOnlyList<string> TabOrder => _settings.TabOrder.ToList();

    /// <summary>Moves a tab (a paired PC or a New PC tab) to another place (a tab dragged); the one order is saved.</summary>
    public void Move(string id, int to)
    {
        var from = _settings.TabOrder.IndexOf(id);
        if (from < 0 || to < 0 || to >= _settings.TabOrder.Count || to == from) return;
        _settings.TabOrder = Reorder.Moved(_settings.TabOrder, new[] { from }, to);
        Save();
        RaiseChanged();
    }

    /// <summary>The display a per-display card of a paired PC drives (empty: all displays).</summary>
    public string ScreenPick(string id, string card) => Find(id)?.ScreenPicks.GetValueOrDefault(card) ?? string.Empty;

    public void SetScreenPick(string id, string card, string display)
    {
        if (Find(id) is not { } pair) return;
        if (display.Length == 0) pair.ScreenPicks.Remove(card); else pair.ScreenPicks[card] = display;
        Save();
    }

    /// <summary>Gives a paired PC a nickname that is kept on this PC only (empty removes it). Everything it shared is listed again under the new name at once.</summary>
    public void Rename(string id, string nickname)
    {
        if (Find(id) is not { } pair) return;
        pair.Nickname = string.IsNullOrWhiteSpace(nickname) ? string.Empty : PcIds.CleanName(nickname);
        Save();
        Reregister(id);
        RaiseChanged();
    }

    /// <summary>Chooses how a paired PC is reached: found on the network, at the address typed for it, or over Tailscale. The typed address is kept whichever is chosen. Choosing Tailscale does not drop the current link: the PC keeps its route until the Tailscale checks of that PC all pass (<see cref="CheckPairTailscale"/>).</summary>
    public void SetRoute(string id, PcRoute route, string address)
    {
        if (Find(id) is not { } pair) return;
        if (pair.Route != route && _links.TryGetValue(id, out var link))
        {
            // The other PC hears it over the working link first; only then this PC switches.
            _ = Guard.RunAsync("Other PCs: route change", async () =>
            {
                var sent = await SendRoute(link, route).ConfigureAwait(false);
                Post(() => ChooseRoute(id, route, address, sent));
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

    /// <summary>Writes every change of a PC's route and its cause to the log; the route is only ever changed by the owner's pick or by pairing again.</summary>
    private void LogRoute(PairedPc pair, PcRoute to, string cause)
    {
        if (pair.Route != to) AppDataStore.AppendLog(Guard.LogFile, $"Other PCs: route of {pair.Name} changed from {pair.Route} to {to} ({cause}).");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WakeReader.Failed -= LogWakeRead;
        WakeReader.Answered -= LogWakeRead;
        _stop.Cancel();
        UnwatchShares();
        _setTimer?.Stop();
        StopNetwork();
        foreach (var link in _links.Values.ToList()) link.Dispose();
        _links.Clear();
        foreach (var pair in _settings.Pairs)
        {
            ForgetRemote(pair.Id);
            _hotkeys.Unregister(WakeActionId(pair.Id));
        }
        _stop.Dispose();
    }
}
