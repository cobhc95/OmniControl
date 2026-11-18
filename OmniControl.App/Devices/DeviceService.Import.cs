using OmniControl.App.Services;

namespace OmniControl.App.Devices;

// Devices shared with another paired PC and devices imported from one (docs/mockups/import-devices.html). The text that travels is
// DeviceShareFormat's; here is only what the list does with it: hand its own out, tell which of another PC's are here, add the chosen ones.
public sealed partial class DeviceService
{
    /// <summary>This PC's device set-ups for another paired PC, without any secret (see <see cref="DeviceShareFormat.Write"/>).</summary>
    public string ShareSetups(bool includeScripts) => DeviceShareFormat.Write(Settings, includeScripts);

    /// <summary>
    /// For "Import settings from another PC": how many of the devices in the imported file need their password typed here (passwords are never sent), as
    /// a short fact such as "2 need passwords"; null when none does or the file cannot be read.
    /// </summary>
    public string? PasswordsNeededIn(IReadOnlyDictionary<string, string> files)
    {
        if (!files.TryGetValue(DataStores.Devices.FileName, out var text)) return null;
        try
        {
            var count = System.Text.Json.JsonSerializer.Deserialize<DeviceSettings>(text)?.Devices.Count(NeedsSignIn) ?? 0;
            return count == 0 ? null : count == 1 ? "1 needs a password" : $"{count} need passwords";
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // the import reports an unreadable file itself
        }
    }

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
