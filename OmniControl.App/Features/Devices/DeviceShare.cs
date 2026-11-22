using System.Text.Json;
using OmniControl.App.Features.Icons;

namespace OmniControl.App.Features.Devices;

/// What a PC hands another paired PC when it is asked for its device set-ups: the devices as they are
/// saved, minus everything secret, and, only when scripts were asked for, the user's own command templates the devices use. It is plain
/// JSON text so the Other PCs link only carries it (it never knows what is inside).
public sealed class DeviceShare
{
    public List<DeviceRecord> Devices { get; set; } = new();

    /// The user's own templates the devices use; empty unless scripts were included.
    public List<ScriptProfile> Profiles { get; set; } = new();
}

/// Writes and reads a <see cref="DeviceShare"/>. Writing leaves out every password (they are in the Credential Manager, never in the records),
/// the account's email, the user's own icon picture, and the text typed by a recorded "type" step (it may be a password). Reading treats the
/// text as untrusted: it checks the shape, cuts every length and count, and refuses a template whose command does not start with kasa
/// (anything else would start any program on this PC).
public static class DeviceShareFormat
{
    private const int DevicesMax = 200, MethodsMax = 20, StepsMax = 200, ProfilesMax = 50, StagesMax = 20, TextMax = 200, CommandMax = 400, IdMax = 64;

    public static string Write(DeviceSettings settings, bool includeScripts)
    {
        var share = new DeviceShare();
        var used = new HashSet<string>();
        foreach (var source in settings.Devices)
        {
            var device = Redacted(source, keepChecks: false);
            foreach (var method in device.Methods)
            {
                if (ScriptProfiles.BuiltIn.Any(p => p.Id == method.ProfileId)) continue;
                if (includeScripts && settings.Profiles.Any(p => p.Id == method.ProfileId)) used.Add(method.ProfileId);
                else method.ProfileId = ScriptProfiles.Auto;
            }
            share.Devices.Add(device);
        }
        share.Profiles = settings.Profiles.Where(p => used.Contains(p.Id)).ToList();
        return JsonSerializer.Serialize(share);
    }

    /// A copy of the device with everything private left out: the account's email, the typed text of recorded steps and the user's own icon
    /// picture (a file only this PC has, whose path can name the user). The auto-check choices are this PC's own, so a share to someone else drops
    /// them; the owner's own paired PC keeps them.
    private static DeviceRecord Redacted(DeviceRecord source, bool keepChecks)
    {
        var device = source.Clone();
        device.IconImage = string.Empty;
        device.ChecksInRule = false; // rules are each PC's own: the PC that takes it makes the device's check rule
        if (!keepChecks)
        {
            device.AutoCheck = null;
            device.AutoCheckOnTray = null;
        }
        if (device.Icon.StartsWith(IconStore.UserPrefix, StringComparison.Ordinal)) device.Icon = string.Empty;
        foreach (var method in device.Methods)
        {
            method.Email = string.Empty;
            foreach (var step in method.Steps.Where(s => s.Kind == SimStepKind.TypeText)) step.Text = string.Empty;
        }
        return device;
    }

    /// The text of devices.json as another PC may read it in a general settings export: the same devices as <see cref="Write"/> hands out
    /// (one rule), everything else of the file as it is. Null when the text is not a devices file (it is then not sent).
    public static string? RedactFile(string devicesJson)
    {
        try
        {
            if (JsonSerializer.Deserialize<DeviceSettings>(devicesJson) is not { } settings) return null;
            settings.Devices = settings.Devices.Select(d => Redacted(d, keepChecks: true)).ToList();
            return JsonSerializer.Serialize(settings);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// The share in <paramref name="json"/>, cleaned; null when it is not one.
    public static DeviceShare? Read(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            if (JsonSerializer.Deserialize<DeviceShare>(json) is not { } read) return null;
            var share = new DeviceShare();
            foreach (var profile in (read.Profiles ?? new()).Take(ProfilesMax))
                if (Clean(profile) is { } ok) share.Profiles.Add(ok);
            foreach (var device in (read.Devices ?? new()).Take(DevicesMax))
                if (Clean(device, share.Profiles) is { } ok) share.Devices.Add(ok);
            return share;
        }
        catch (JsonException)
        {
            return null; // not a share: the caller says so
        }
    }

    private static DeviceRecord? Clean(DeviceRecord device, IReadOnlyList<ScriptProfile> profiles)
    {
        if (Clip(device.Id, IdMax).Length == 0) return null;
        var clean = new DeviceRecord
        {
            Id = Clip(device.Id, IdMax), OriginId = Clip(device.OriginId, IdMax), Name = Clip(device.Name), IconColour = Clip(device.IconColour, 20),
            Icon = IconLibrary.Find(device.Icon) is { } icon ? icon.Id : string.Empty,
        };
        foreach (var method in (device.Methods ?? new()).Take(MethodsMax))
        {
            var kind = DeviceMethods.Known(method.Kind);
            var profile = Clip(method.ProfileId, IdMax);
            clean.Methods.Add(new MethodRecord
            {
                Kind = kind, Address = Clip(method.Address), ProfileId = ScriptProfiles.BuiltIn.Any(p => p.Id == profile) || profiles.Any(p => p.Id == profile) ? profile : ScriptProfiles.Auto,
                StagesDone = (method.StagesDone ?? new()).Take(StagesMax).Select(s => Clip(s, IdMax)).ToList(), Reserved = method.Reserved, Verified = method.Verified,
                EmulatorId = Clip(method.EmulatorId, IdMax), AdbAddress = Clip(method.AdbAddress), SimAppReady = method.SimAppReady, SimVerified = method.SimVerified,
                Taught = method.Taught ?? new StateModel(), Steps = (method.Steps ?? new()).Take(StepsMax).Select(CleanStep).ToList(),
            });
        }
        return clean.Methods.Count == 0 ? null : clean;
    }

    private static SimStep CleanStep(SimStep step) => new()
    {
        Kind = Enum.IsDefined(step.Kind) && step.Kind != SimStepKind.TypeText ? step.Kind : SimStepKind.Check, Package = Clip(step.Package), Text = Clip(step.Text), Description = Clip(step.Description),
        ResourceId = Clip(step.ResourceId), Direction = Clip(step.Direction, 10), Label = Clip(step.Label), Anchor = Clip(step.Anchor), X = step.X, Y = step.Y,
    };

    private static ScriptProfile? Clean(ScriptProfile profile)
    {
        if (!KasaCommand.RunsKasa(profile.On) || !KasaCommand.RunsKasa(profile.Off) || (profile.State.Trim().Length > 0 && !KasaCommand.RunsKasa(profile.State))) return null;
        var clean = new ScriptProfile
        {
            Id = Clip(profile.Id, IdMax), Name = Clip(profile.Name), On = Clip(profile.On, CommandMax), Off = Clip(profile.Off, CommandMax), State = Clip(profile.State, CommandMax),
            Icon = IconLibrary.Find(profile.Icon) is { } icon ? icon.Id : IconLibrary.Plug, BasedOn = Clip(profile.BasedOn, IdMax), Brief = Clip(profile.Brief), MenuTag = Clip(profile.MenuTag, 20),
            NeedsSignIn = profile.NeedsSignIn, SignInNote = Clip(profile.SignInNote), LowerCaseEmail = profile.LowerCaseEmail,
            Stages = (profile.Stages ?? new()).Take(StagesMax).Select(s => new TemplateStage(Clip(s.Id, IdMax), Clip(s.Title), Clip(s.Text, 600), Clip(s.Confirm))).ToList(),
            Tips = (profile.Tips ?? new()).Take(StagesMax).Select(t => new ErrorTip(Clip(t.Match), Clip(t.Tip, 600))).ToList(),
        };
        return clean.Id.Length == 0 ? null : clean;
    }

    private static string Clip(string? text, int max = TextMax) => TextClip.Clean(text, max);
}
