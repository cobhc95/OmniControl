using System.Security;
using Microsoft.Win32;

namespace OmniControl.Core.Engine.Apo;

/// <summary>
/// Works out the install and uninstall plans for the OmniControl APO by reading the registry only (the same rules as Equalizer
/// APO's Device Selector, DeviceAPOInfo.cpp at bbfcc3e: which effect slots to use, keeping the vendor's APO as a "child" so its
/// own effects keep working, and remembering every original value so uninstall can put it back). Nothing here writes.
/// </summary>
public static class ApoPlanner
{
    // Device Selector's markers and names (DeviceAPOInfo.cpp / .h), read by the forked APO's DeviceAPOInfo::load.
    private const string NoKey = "!KEY", NoValue = "!VALUE", InstallVersion = "2";
    private const string PreMixChild = "PreMixChild", PostMixChild = "PostMixChild", Version = "Version", AllowSilent = "AllowSilentBufferModification";
    private const string MultiSfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},13", MultiMfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},14",
        MultiEfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},15";
    private const string SfxModes = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},5", MfxModes = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},6",
        EfxModes = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},7", DefaultMode = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";
    private const string CombinedDevice = "{b3f8fa53-0004-438e-9003-51a46e139bfc},41";
    private const string FriendlyName = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";

    /// <summary>Stored in our key: the DisableProtectedAudioDG value before the first install ("absent" when there was none).</summary>
    internal const string OriginalDgValue = "OriginalDisableProtectedAudioDG", Absent = "absent";

    /// <summary>Stored per device: the "audio enhancements off" value the install removed.</summary>
    private const string OriginalEnhancements = "OriginalDisableEnhancements";

    private enum Slots { LfxGfx, SfxMfx, SfxEfx }

    /// <summary>
    /// Install on one device, from the built files in <paramref name="sourceFolder"/>. The device's key is found under Render or
    /// Capture by Equalizer APO's own rule (<see cref="ApoStatus.EndpointKey"/>); OmniControl offers it for the default playback device only.
    /// </summary>
    public static ApoPlan PlanInstall(string sourceFolder, string endpointId)
    {
        var steps = new List<ApoStep>();
        var problems = new List<string>();
        var device = ApoIds.DeviceKeyOf(endpointId);
        var dll = Path.Combine(sourceFolder, ApoIds.DllName);
        if (!File.Exists(dll))
            problems.Add($"The OmniControl APO is not built: {dll} is missing. Build it with native/EqualizerAPO/build.ps1 (see its README).");

        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            var endpointKey = ApoStatus.EndpointKey(machine, device);
            using var deviceKey = machine.OpenSubKey(endpointKey);
            using var fx = machine.OpenSubKey($@"{endpointKey}\FxProperties");
            using var props = deviceKey?.OpenSubKey("Properties");
            var name = props?.GetValue(FriendlyName) as string ?? device;
            if (deviceKey is null) problems.Add($"Audio device {device} was not found.");
            else if (fx is null) problems.Add($"\"{name}\" has no effects slots Windows lets us change safely (no FxProperties key); not supported.");
            var slots = ApoStatus.SlotsOf(machine, endpointKey);
            if (slots.Any(ApoIds.IsUpstream)) problems.Add($"Equalizer APO is already on \"{name}\". Remove it with its own Device Selector first: only one can be there.");
            if (slots.Any(ApoIds.IsOurs)) problems.Add($"OmniControl APO is already on \"{name}\". Remove it first to install again.");

            AddCommonInstallSteps(machine, steps, sourceFolder);
            if (fx is not null) AddDeviceSteps(steps, fx, device, endpointKey, name, deviceKey!);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            problems.Add($"Couldn't read the audio settings: {ex.Message}");
        }

        steps.Add(new ApoStep(ApoStepKind.RestartAudioService, "AudioSrv", null, null,
            "Restart the Windows Audio service (all sound stops for a few seconds)."));
        return new ApoPlan($"Install OmniControl APO on playback device {device}", steps, problems);
    }

    private static void AddCommonInstallSteps(RegistryKey machine, List<ApoStep> steps, string sourceFolder)
    {
        var install = ApoIds.InstallFolder;
        var config = ApoIds.ConfigFolder;
        if (Directory.Exists(sourceFolder))
            foreach (var file in Directory.GetFiles(sourceFolder, "*.dll"))
                steps.Add(new ApoStep(ApoStepKind.CopyFile, Path.Combine(install, Path.GetFileName(file)), file, null,
                    $"Copy {Path.GetFileName(file)} to {install}."));
        steps.Add(new ApoStep(ApoStepKind.CreateSharedFolder, config, null, null, $"Create {config} and let the Users group write to it (OmniControl writes the effects there)."));
        steps.Add(new ApoStep(ApoStepKind.WriteFile, Path.Combine(config, ApoIds.ConfigFileName), null, ApoIds.EmptyConfig,
            $"Write an empty effects file {ApoIds.ConfigFileName} (sound passes unchanged until OmniControl writes the chain)."));
        steps.Add(Set(ApoIds.AppKey, "InstallPath", install, "Remember the install folder."));
        steps.Add(Set(ApoIds.AppKey, "ConfigPath", config, "Tell the APO where its effects file is."));
        steps.Add(Set(ApoIds.AppKey, "EnableTrace", "false", "Keep the APO's trace log off."));

        using var audio = machine.OpenSubKey(ApoIds.AudioKey);
        using var app = machine.OpenSubKey(ApoIds.AppKey);
        var dg = audio?.GetValue(ApoIds.ProtectedAudioValue);
        if (app?.GetValue(OriginalDgValue) is null)
            steps.Add(Set(ApoIds.AppKey, OriginalDgValue, dg?.ToString() ?? Absent,
                $"Remember that {ApoIds.ProtectedAudioValue} was {dg?.ToString() ?? "not set"}, so uninstall can put it back."));
        if (dg is not int v || v != 1)
            steps.Add(new ApoStep(ApoStepKind.SetDword, ApoIds.AudioKey, ApoIds.ProtectedAudioValue, 1,
                $"Set HKLM\\{ApoIds.AudioKey}\\{ApoIds.ProtectedAudioValue} = 1 (lets Windows load an APO without a Microsoft signature; weakens the protected audio path)."));
        steps.Add(new ApoStep(ApoStepKind.RegisterDll, Path.Combine(install, ApoIds.DllName), null, null,
            $"Register {ApoIds.DllName} (regsvr32): COM classes {ApoIds.PreMixClsid} and {ApoIds.PostMixClsid}."));
    }

    /// <summary>The device's slots: save every original, keep the vendor APO as a child, then put ours in.</summary>
    private static void AddDeviceSteps(List<ApoStep> steps, RegistryKey fx, string device, string endpointKey, string name, RegistryKey deviceKey)
    {
        var fxPath = $@"{endpointKey}\FxProperties";
        var childPath = $@"{ApoIds.ChildApoKey}\{device}";
        var originals = ApoIds.FxSlots.Select(n => fx.GetValue(n) as string ?? NoValue).ToArray();
        for (var i = 0; i < ApoIds.FxSlots.Count; i++)
            steps.Add(Set(childPath, ApoIds.FxSlots[i], originals[i], $"Remember \"{name}\" slot {SlotName(i)} = {originals[i]}."));

        var layout = LayoutOf(fx, deviceKey);
        var (pre, post) = Children(layout, originals);
        steps.Add(Set(childPath, PreMixChild, pre, $"Keep the vendor's pre-mix effect {Show(pre)} running inside OmniControl APO."));
        steps.Add(Set(childPath, PostMixChild, post, $"Keep the vendor's post-mix effect {Show(post)} running inside OmniControl APO."));
        steps.Add(Set(childPath, AllowSilent, "false", "Leave silent buffers alone (Equalizer APO's default)."));
        steps.Add(Set(childPath, Version, InstallVersion, "Mark the layout version the APO understands."));

        var (preSlot, postSlot, remove, modes) = layout switch
        {
            Slots.LfxGfx => (ApoIds.Lfx, ApoIds.Gfx, new[] { ApoIds.Sfx, ApoIds.Mfx, ApoIds.Efx }, (Pre: (string?)null, Post: (string?)null)),
            Slots.SfxMfx => (ApoIds.Sfx, ApoIds.Mfx, new[] { ApoIds.Lfx, ApoIds.Gfx }, (Pre: SfxModes, Post: MfxModes)),
            _ => (ApoIds.Sfx, ApoIds.Efx, new[] { ApoIds.Lfx, ApoIds.Gfx }, (Pre: SfxModes, Post: EfxModes)),
        };
        foreach (var slot in remove.Where(s => fx.GetValue(s) is not null))
            steps.Add(new ApoStep(ApoStepKind.DeleteValue, fxPath, slot, null, $"Clear \"{name}\" slot {SlotName(slot)} (saved above)."));
        steps.Add(Set(fxPath, preSlot, ApoIds.PreMixClsid, $"Put OmniControl APO (pre-mix) in \"{name}\" slot {SlotName(preSlot)}."));
        steps.Add(Set(fxPath, postSlot, ApoIds.PostMixClsid, $"Put OmniControl APO (post-mix) in \"{name}\" slot {SlotName(postSlot)}."));
        foreach (var modeValue in new[] { modes.Pre, modes.Post }.OfType<string>().Where(m => fx.GetValue(m) is null))
            steps.Add(new ApoStep(ApoStepKind.SetMultiString, fxPath, modeValue, new[] { DefaultMode }, "Declare the default processing mode for the new slot."));
        if (fx.GetValue(ApoIds.DisableEnhancements) is int off)
        {
            steps.Add(Set(childPath, OriginalEnhancements, off.ToString(), "Remember that audio enhancements were turned off."));
            steps.Add(new ApoStep(ApoStepKind.DeleteValue, fxPath, ApoIds.DisableEnhancements, null,
                $"Turn \"Audio enhancements\" back on for \"{name}\" (effects only run with them on)."));
        }
    }

    /// <summary>Device Selector's choice for Windows 8.1 and later: LFX/GFX only when the driver itself uses only those.</summary>
    private static Slots LayoutOf(RegistryKey fx, RegistryKey deviceKey)
    {
        bool Has(string v) => fx.GetValue(v) is not null;
        if ((Has(ApoIds.Lfx) || Has(ApoIds.Gfx)) && !new[] { ApoIds.Sfx, ApoIds.Mfx, ApoIds.Efx, MultiSfx, MultiMfx, MultiEfx }.Any(Has))
            return Slots.LfxGfx;
        using var props = deviceKey.OpenSubKey("Properties");
        return props?.GetValue(CombinedDevice) is not null ? Slots.SfxMfx : Slots.SfxEfx;
    }

    /// <summary>The vendor APOs to run as children (DeviceAPOInfo::getOriginalAPOPreMix / PostMix).</summary>
    private static (string Pre, string Post) Children(Slots layout, string[] o)
    {
        const int L = 0, G = 1, S = 2, M = 3, E = 4;
        string pre, post;
        switch (layout)
        {
            case Slots.LfxGfx:
                var empty = o[L] == NoValue && o[G] == NoValue;
                pre = empty ? o[S] : o[L];
                post = empty ? o[M] : o[G];
                break;
            case Slots.SfxMfx:
                var emptySm = o[S] == NoValue && o[M] == NoValue;
                pre = emptySm ? o[L] : o[S];
                post = emptySm ? o[G] : o[M];
                break;
            default:
                var emptySe = o[S] == NoValue && o[E] == NoValue;
                pre = emptySe ? o[L] : o[S];
                post = emptySe ? o[G] : o[E];
                break;
        }
        static string Clean(string g) => g is NoKey or NoValue ? string.Empty : g;
        return (Clean(pre), Clean(post));
    }

    /// <summary>Removes OmniControl APO from every device it is on and puts every original back.</summary>
    public static ApoPlan PlanUninstall()
    {
        var steps = new List<ApoStep>();
        var problems = new List<string>();
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var children = machine.OpenSubKey(ApoIds.ChildApoKey);
            foreach (var device in children?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var saved = children!.OpenSubKey(device);
                var fxPath = $@"{ApoStatus.EndpointKey(machine, device)}\FxProperties";
                foreach (var slot in ApoIds.FxSlots)
                {
                    var original = saved?.GetValue(slot) as string;
                    if (original is null or NoKey) continue;
                    steps.Add(original == NoValue
                        ? new ApoStep(ApoStepKind.DeleteValue, fxPath, slot, null, $"Clear device {device} slot {SlotName(slot)} (it was empty).")
                        : Set(fxPath, slot, original, $"Put back device {device} slot {SlotName(slot)} = {original}."));
                }
                if (saved?.GetValue(OriginalEnhancements) is string off && int.TryParse(off, out var offValue))
                    steps.Add(new ApoStep(ApoStepKind.SetDword, fxPath, ApoIds.DisableEnhancements, offValue, $"Turn audio enhancements of device {device} off again, as they were."));
            }

            using var app = machine.OpenSubKey(ApoIds.AppKey);
            var dll = Path.Combine(ApoIds.InstallFolder, ApoIds.DllName);
            if (File.Exists(dll))
                steps.Add(new ApoStep(ApoStepKind.UnregisterDll, dll, null, null, $"Unregister {ApoIds.DllName} (regsvr32 /u)."));
            AddRestoreDg(machine, app, steps);
            if (app is not null) steps.Add(new ApoStep(ApoStepKind.DeleteKey, ApoIds.AppKey, null, null, $"Delete HKLM\\{ApoIds.AppKey}."));
            if (Directory.Exists(ApoIds.InstallFolder))
                steps.Add(new ApoStep(ApoStepKind.DeleteFolder, ApoIds.InstallFolder, null, null, $"Delete {ApoIds.InstallFolder}."));
            if (Directory.Exists(ApoIds.ConfigFolder))
                steps.Add(new ApoStep(ApoStepKind.DeleteFolder, ApoIds.ConfigFolder, null, null, $"Delete {ApoIds.ConfigFolder}."));
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            problems.Add($"Couldn't read the audio settings: {ex.Message}");
        }

        if (steps.Count > 0)
            steps.Add(new ApoStep(ApoStepKind.RestartAudioService, "AudioSrv", null, null,
                "Restart the Windows Audio service (all sound stops for a few seconds)."));
        return new ApoPlan("Remove OmniControl APO and put the original audio settings back", steps, problems);
    }

    /// <summary>Puts DisableProtectedAudioDG back, unless Equalizer APO itself is installed and still needs it.</summary>
    private static void AddRestoreDg(RegistryKey machine, RegistryKey? app, List<ApoStep> steps)
    {
        if (app?.GetValue(OriginalDgValue) is not string original) return;
        if (ApoStatus.UpstreamInstallFolder(machine) is not null)
        {
            steps.Add(new ApoStep(ApoStepKind.DeleteValue, ApoIds.AppKey, OriginalDgValue, null,
                $"Leave {ApoIds.ProtectedAudioValue} as it is: Equalizer APO is installed and needs it."));
            return;
        }
        steps.Add(original == Absent
            ? new ApoStep(ApoStepKind.DeleteValue, ApoIds.AudioKey, ApoIds.ProtectedAudioValue, null, $"Remove {ApoIds.ProtectedAudioValue} (it was not set before).")
            : new ApoStep(ApoStepKind.SetDword, ApoIds.AudioKey, ApoIds.ProtectedAudioValue, int.TryParse(original, out var v) ? v : 0,
                $"Put {ApoIds.ProtectedAudioValue} back to {original}."));
    }

    private static ApoStep Set(string key, string name, string value, string description) =>
        new(ApoStepKind.SetString, key, name, value, description);

    private static string SlotName(int index) => new[] { "LFX", "GFX", "SFX", "MFX", "EFX" }[index];
    private static string SlotName(string valueName) => SlotName(ApoIds.FxSlots.ToList().IndexOf(valueName));
    private static string Show(string guid) => guid.Length == 0 ? "(none)" : guid;
}
