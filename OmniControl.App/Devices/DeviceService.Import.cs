namespace OmniControl.App.Devices;

// Devices shared with another paired PC and devices imported from one (docs/mockups/import-devices.html). The text that travels is
// DeviceShareFormat's; here is only what the list does with it: hand its own out, tell which of another PC's are here, add the chosen ones.
public sealed partial class DeviceService
{
    /// <summary>This PC's device set-ups for another paired PC, without any secret (see <see cref="DeviceShareFormat.Write"/>).</summary>
    public string ShareSetups(bool includeScripts) => DeviceShareFormat.Write(Settings, includeScripts);

    /// <summary>True when the device of another PC is already in the list: it was imported before, or came from here.</summary>
    public bool IsHere(DeviceRecord remote) =>
        Settings.Devices.Any(d => d.Id == remote.Id || (d.OriginId.Length > 0 && d.OriginId == remote.Id) || (remote.OriginId.Length > 0 && d.Id == remote.OriginId));

    /// <summary>
    /// Adds the chosen devices of another PC as new devices: new ids for each device and method (the first id is kept as <see cref="DeviceRecord.OriginId"/>),
    /// no password and no email (typed again in the set-up), and the templates they use as new templates of the user's (one that matches an existing
    /// template is reused). Returns the devices added.
    /// </summary>
    public IReadOnlyList<DeviceRecord> Import(IReadOnlyList<DeviceRecord> devices, IReadOnlyList<ScriptProfile> profiles)
    {
        var added = new List<DeviceRecord>();
        var profileIds = new Dictionary<string, string>();
        foreach (var profile in profiles.Where(p => devices.Any(d => d.Methods.Any(m => m.ProfileId == p.Id))))
        {
            var same = Settings.Profiles.FirstOrDefault(p => p.Name == profile.Name && p.On == profile.On && p.Off == profile.Off && p.State == profile.State);
            if (same is null) Settings.Profiles.Add(same = profile.AsOwn(profile.Name));
            profileIds[profile.Id] = same.Id;
        }
        foreach (var source in devices)
        {
            var device = source.Clone();
            device.OriginId = source.Id;
            device.Id = Guid.NewGuid().ToString("N");
            device.IconImage = string.Empty;
            foreach (var method in device.Methods)
            {
                method.Id = Guid.NewGuid().ToString("N");
                method.Email = string.Empty;
                if (profileIds.TryGetValue(method.ProfileId, out var own)) method.ProfileId = own;
            }
            Settings.Devices.Add(device);
            RegisterActions(device);
            added.Add(device);
        }
        if (added.Count == 0) return added;
        Edited();
        ListChanged?.Invoke();
        return added;
    }
}
