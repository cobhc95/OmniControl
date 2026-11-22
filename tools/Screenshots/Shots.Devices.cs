using OmniControl.App.Features.Devices;
using OmniControl.App.Features.Devices.Pages;
using OmniControl.App.Features.OtherPcs;
using OmniControl.Core.Interfaces;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    /// The Smart Devices page with three sample plugs (Scripts method, set up, documentation addresses). Nothing reaches a device: the
    /// auto-checks are never started, the page's probes (Python, adb) run only when it is shown (never here), the simulation timers that
    /// look for adb are stopped at once, and passwords go to an in-memory store.
    private static void Devices(Rig rig, Camera camera)
    {
        Use(ViewTemplate.For<DevicesPageViewModel, DevicesPage>(), ViewTemplate.For<DeviceItem, DeviceDetail>());

        var settings = new DeviceSettings { AutoCheckOnTray = false };
        settings.Devices.Add(Plug("Desk lamp", "lamp", "192.0.2.21", ScriptProfiles.Tapo));
        settings.Devices.Add(Plug("Monitor light", "bulb", "192.0.2.22", ScriptProfiles.Tapo));
        settings.Devices.Add(Plug("Speakers", "plug", "192.0.2.23", ScriptProfiles.Kasa));
        var service = new DeviceService(rig.Actions, rig.States, rig.Hardware, new MemoryCredentials(), Rig.Report, _ => { }, settings);
        service.Simulation.Dispose(); // its timers would look for adb and ask it for devices
        var page = new DevicesPageViewModel(service, Rig.Ask, Rig.Report, (_, _) => Task.FromResult<App.Features.Icons.IconPick?>(null), new NoPcs(), () => { }, () => { });
        rig.Pages.Add(page);
        camera.Page(page, "smart-devices");
    }

    private static DeviceRecord Plug(string name, string icon, string address, string profile) => new()
    {
        Name = name,
        Icon = icon,
        Methods = { new MethodRecord { Kind = DeviceMethods.Scripts, Address = address, ProfileId = profile, Email = "you@example.com", Reserved = true, Verified = true } },
    };

    private sealed class MemoryCredentials : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new();
        public bool Write(string target, string secret) => _secrets.TryAdd(target, secret) || true;
        public string? Read(string target) => _secrets.GetValueOrDefault(target);
        public void Delete(string target) => _secrets.Remove(target);
    }

    private sealed class NoPcs : IPairedPcs
    {
        public IReadOnlyList<PairedPc> Pairs => Array.Empty<PairedPc>();
        public PairedPc? Find(string id) => null;
        public event Action? Changed { add { } remove { } }
        public bool IsOnline(string id) => false;
        public string SeenWords(string id) => string.Empty;
        public string? KnownDeviceSetups(string pcId) => null;
        public Task<DeviceSetupsReply> RequestDeviceSetupsAsync(string pcId, bool includeScripts) => throw new InvalidOperationException("No paired PCs.");
    }
}
