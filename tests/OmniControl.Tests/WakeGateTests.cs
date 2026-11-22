using System.Collections.ObjectModel;
using OmniControl.App.Features.OtherPcs;
using OmniControl.App.Shared.Contracts;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.Tests;

/// The wake signal leaves only for a person's fresh press of Wake for that very PC (PcWake through WakeGate), headless: the packet sender is a
/// fake that only records, this PC's wake facts are given, and no network, adapter or Windows setting is touched.
public class WakeGateTests
{
    private const string DeskMac = "AA:BB:CC:DD:EE:01", LapMac = "AA:BB:CC:DD:EE:02", Subnet = "192.168.1.0/24";

    private sealed class FakeBook : IPcBook
    {
        public readonly List<PairedPc> List = new();
        public readonly HashSet<string> Online = new();
        public PairedPc? Find(string id) => List.FirstOrDefault(p => p.Id == id);
        public IReadOnlyList<PairedPc> Pairs => List;
        public bool IsOnline(string id) => Online.Contains(id);
        public PcLink? LinkOf(string id) => null;
        public IReadOnlyList<PcLink> Links => Array.Empty<PcLink>();
        public void Save() { }
        public void RaiseChanged() { }
    }

    private sealed class FakeActions : IActionRegistry
    {
        public ObservableCollection<HotkeyAction> Actions { get; } = new();
        public IEnumerable<HotkeyAction> PickerActions => Actions;
        public event Action? BindingsSaved { add { } remove { } }
        public event Action<IReadOnlyDictionary<string, string>>? Moved { add { } remove { } }
        public void Register(HotkeyAction action) => Actions.Add(action);
        public void Register(IEnumerable<HotkeyAction> actions) { foreach (var a in actions) Actions.Add(a); }
        public void RegisterPickerCommand(HotkeyAction command) { }
        public void Unregister(string id) { foreach (var gone in Actions.Where(a => a.Id == id).ToList()) Actions.Remove(gone); }
        public string? ProblemWith(string gesture, string id) => null;
        public void Move(IReadOnlyDictionary<string, string> moved) { }
        public HotkeyAction? Find(string id) => Actions.FirstOrDefault(a => a.Id == id);
        public ActionSection? SectionOf(string section) => null;
        public HotkeyAction? OwnerOf(string gesture, string exceptId) => null;
    }

    private sealed class Rig : IDisposable
    {
        public readonly FakeBook Book = new();
        public readonly FakeActions Actions = new();
        public readonly List<string> Sent = new();
        public readonly List<string> Said = new();
        public DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        public readonly PcWake Wake;

        public Rig()
        {
            Book.List.Add(Asleep("desk", DeskMac));
            Book.List.Add(Asleep("lap", LapMac));
            // The fake "network": records the packet, and the PC it names answers at once.
            var gate = new WakeGate((mac, _) =>
            {
                lock (Sent) Sent.Add(mac);
                if (Book.List.FirstOrDefault(p => p.Wake?.Mac == mac) is { } woken) Book.Online.Add(woken.Id);
                return null;
            }, () => Now);
            Wake = new PcWake(Book, new PcRequests(), Actions, Said.Add, _ => Task.FromResult(false), "Here", gate, () => Facts(string.Empty));
            foreach (var pair in Book.List) Wake.Register(pair);
        }

        /// This PC's own facts are read (from the fake) so it is on the same network as the paired PCs.
        public Task ReadOwnAsync() => Wake.CheckOwnWakeAsync();

        public void Dispose() => Wake.Dispose();
    }

    private static WakeFacts Facts(string mac) => new()
    {
        Known = true, Adapter = "Wi-Fi", Mac = mac.Length > 0 ? mac : "AA:BB:CC:DD:EE:00", Subnet = Subnet,
        CardCanWake = true, MagicPacketOn = true, Armed = true, MagicOnly = true,
    };

    private static PairedPc Asleep(string id, string mac) => new() { Id = id, Name = id, Wake = Facts(mac), Away = WakeAdvices.Sleep, AwayAt = DateTime.Now };

    // ---------------------------------------------------------------- the person's press

    [Fact]
    public async Task PressingWake_SendsOneSignal_ToThatPcOnly()
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();

        var status = await rig.Wake.WakeAsync("desk", rig.Wake.GrantWake("desk"));

        Assert.Equal(new[] { DeskMac }, rig.Sent);
        Assert.Equal(WakePhase.Idle, status.Phase);
        Assert.Contains(rig.Said, s => s == "desk is on.");
    }

    [Fact]
    public async Task WakeAction_IsPersonOnly_AndItsKeySendsTheSignal()
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();
        var action = rig.Actions.Find("otherpcs.wake.desk")!;

        Assert.True(action.PersonOnly);
        Assert.Null(action.Common); // never offered to another PC
        action.Run();
        for (var i = 0; i < 100 && rig.Sent.Count == 0; i++) await Task.Delay(20);

        Assert.Equal(new[] { DeskMac }, rig.Sent);
    }

    // ---------------------------------------------------------------- what may not send

    [Fact]
    public async Task NoGrant_SendsNothing_AndSaysSoOnce()
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();

        var status = await rig.Wake.WakeAsync("desk", null);

        Assert.Empty(rig.Sent);
        Assert.Equal(WakePhase.Failed, status.Phase);
        Assert.False(status.Sent);
        Assert.Single(rig.Said, s => s.StartsWith("Couldn't wake desk", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExpiredGrant_SendsNothing()
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();
        var grant = rig.Wake.GrantWake("desk");
        rig.Now += Timing.WakeGrantLife + TimeSpan.FromSeconds(1);

        await rig.Wake.WakeAsync("desk", grant);

        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task GrantForAnotherPc_SendsNothing()
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();

        await rig.Wake.WakeAsync("desk", rig.Wake.GrantWake("lap"));

        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task UsedGrant_IsNotUsedTwice()
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();
        var grant = rig.Wake.GrantWake("desk");
        await rig.Wake.WakeAsync("desk", grant);
        rig.Book.Online.Remove("desk"); // it went back to sleep

        var again = await rig.Wake.WakeAsync("desk", grant);

        Assert.Single(rig.Sent);
        Assert.Equal(WakePhase.Failed, again.Phase);
        Assert.False(again.Sent);
    }

    [Fact]
    public void Gate_TakesAGrantOnceForItsPcWhileFresh()
    {
        var now = DateTime.UtcNow;
        var gate = new WakeGate((_, _) => null, () => now);
        var desk = gate.Grant("desk", Timing.WakeGrantLife);

        Assert.False(gate.TryUse(desk, "lap"));
        Assert.True(gate.TryUse(desk, "desk"));
        Assert.False(gate.TryUse(desk, "desk"));
        Assert.False(gate.TryUse(null, "desk"));
        var older = gate.Grant("desk", Timing.WakeGrantLife);
        var newer = gate.Grant("desk", Timing.WakeGrantLife);
        Assert.False(gate.TryUse(older, "desk")); // a newer press replaced it
        now += Timing.WakeGrantLife;
        Assert.False(gate.TryUse(newer, "desk"));
    }

    [Fact]
    public void RulesChainsAndOtherPcs_CannotRunTheWakeAction()
    {
        using var rig = new Rig();
        var said = new List<string>();
        using var registry = new HotkeyRegistry(new HardwareWatches(), said.Add);
        var action = rig.Actions.Find("otherpcs.wake.desk")!;

        Assert.True(registry.Permitted(action, null)); // a key, a mouse button, a tile or Run now
        Assert.False(registry.Permitted(action, RunChain.Start("rule.morning"))); // a rule's trigger, or start-up
        Assert.False(registry.Permitted(action, RunChain.Start("rule.mine", byPerson: true))); // a rule a person started
        Assert.False(registry.Permitted(action, RunChain.After(null, PcProtocol.RemotePrefix + "lap", byPerson: true))); // a request from another PC
        Assert.Equal(3, said.Count);
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task OldRelayAsk_FromAnotherPc_SendsNothing()
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();
        var key = new byte[32];
        var stream = new MemoryStream();
        using var link = new PcLink("lap", stream, key, key, dialled: false);

        rig.Wake.HandleAsk(link, new PcMsg { T = PcMsgType.WakeAsk, Id = "relay", Rid = 1, Text = "desk" });
        for (var i = 0; i < 100 && stream.Length == 0; i++) await Task.Delay(20); // the refusal was written back

        Assert.True(stream.Length > 0);
        Assert.Empty(rig.Sent);
    }

    // A paired PC ("lap") asks this one to relay a wake; the answer is written back to its link (a stream here).
    private static async Task AskRelayAsync(Rig rig, string target)
    {
        var key = new byte[32];
        var stream = new MemoryStream();
        using var link = new PcLink("lap", stream, key, key, dialled: false);
        rig.Wake.HandleAsk(link, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeRelay, Rid = 1, Text = target });
        for (var i = 0; i < 100 && stream.Length == 0; i++) await Task.Delay(20);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task RelayOfAPressMadeThere_SendsOnePacket_ToTheCardThisPcKnows()
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();

        await AskRelayAsync(rig, "desk");

        Assert.Equal(new[] { DeskMac }, rig.Sent);
    }

    [Fact]
    public async Task Relay_FromAPcWithoutPowerPermission_SendsNothing()
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();
        rig.Book.Find("lap")!.MayRunPower = false;

        await AskRelayAsync(rig, "desk");

        Assert.Empty(rig.Sent);
    }

    [Theory]
    [InlineData(DeskMac)] // an address instead of a paired PC
    [InlineData("tv")] // a PC this one is not paired with
    [InlineData("lap")] // the asking PC itself
    public async Task Relay_OnlyForAnotherPairedPc(string target)
    {
        using var rig = new Rig();
        await rig.ReadOwnAsync();

        await AskRelayAsync(rig, target);

        Assert.Empty(rig.Sent);
    }
}
