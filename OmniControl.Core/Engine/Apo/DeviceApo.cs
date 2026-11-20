namespace OmniControl.Core.Engine.Apo;

/// Which effect slots Equalizer APO takes on a device (Device Selector's install modes).
public enum ApoInstallMode { LfxGfx = 0, SfxMfx = 1, SfxEfx = 2 }

/// How Equalizer APO is put on a device (Device Selector's InstallState and its troubleshooting options).
/// <param name="PreMix">Install the pre-mix APO (LFX or SFX).</param>
/// <param name="PostMix">Install the post-mix APO (GFX, MFX or EFX); playback devices only.</param>
/// <param name="OriginalPreMix">Keep the driver's own pre-mix APO running inside Equalizer APO ("Use original APO").</param>
/// <param name="OriginalPostMix">The same for the post-mix APO.</param>
/// <param name="AllowSilentBuffer">"Allow silent buffer modification".</param>
/// <param name="AutoAdjust">Device Selector may try the other modes when the check fails (stored as DisableAutomaticAdjustment).</param>
public sealed record ApoInstallState(ApoInstallMode Mode, bool PreMix, bool PostMix, bool OriginalPreMix, bool OriginalPostMix, bool AllowSilentBuffer, bool AutoAdjust);

/// One audio endpoint as Equalizer APO's Device Selector sees it, and putting Equalizer APO on it or taking it off: a line-for-line port of
/// DeviceAPOInfo::load, getOriginalAPOPreMix/PostMix, install and uninstall (Equalizer APO 1.4.2, DeviceAPOInfo.cpp, GPL-2.0-or-later,
/// Copyright (C) 2012 Jonas Thedering), so a device OmniControl attaches is attached exactly as Device Selector would do it and Equalizer APO's
/// own Device Selector (and APO) read it the same way. Windows 8.1 or later is assumed (OmniControl needs Windows 10), so the branches for
/// older Windows are left out; the APO's class ids are compared without regard to case.
public sealed class DeviceApo
{
    private DeviceApo(string guid, bool input) => (Guid, Input) = (guid, input);

    /// The device's key name under MMDevices ({guid}).
    public string Guid { get; }

    /// A recording device (its key is under Capture).
    public bool Input { get; }

    public bool Disabled { get; private set; }
    public bool Unplugged { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Connection { get; private set; } = string.Empty;
    public bool EnhancementsOff { get; private set; }

    /// Equalizer APO is in one of its slots.
    public bool Installed { get; private set; }

    /// The version of the record under Child APOs ("0" when not installed, "1" for an old one without the Version value).
    public string Version { get; private set; } = "0";

    /// LFX, GFX, SFX, MFX, EFX as they were before Equalizer APO: a class id, <see cref="ApoIds.NoValue"/> or <see cref="ApoIds.NoKey"/>.
    public string[] Original { get; } = new string[ApoIds.FxSlots.Count];

    public string PreMixChildGuid { get; private set; } = string.Empty;
    public string PostMixChildGuid { get; private set; } = string.Empty;

    /// How it is installed now; for a device without Equalizer APO, how Device Selector would install it.
    public ApoInstallState Current { get; private set; } = null!;

    private const int Lfx = 0, Gfx = 1, Sfx = 2, Mfx = 3, Efx = 4;

    private string KeyPath => $@"{(Input ? ApoIds.CaptureKey : ApoIds.RenderKey)}\{Guid}";
    private string FxKey => KeyPath + @"\FxProperties";
    private string ChildKey => $@"{ApoIds.ChildApoKey}\{Guid}";

    /// Every device under Render (or Capture) that is present (DeviceAPOInfo::loadAllInfos); one a newer Equalizer APO attached is left out.
    internal static IReadOnlyList<DeviceApo> LoadAll(IApoRegistry reg, bool input) =>
        reg.SubKeys(input ? ApoIds.CaptureKey : ApoIds.RenderKey).Select(guid => LoadKnown(reg, guid)).OfType<DeviceApo>().ToList();

    private static DeviceApo? LoadKnown(IApoRegistry reg, string guid)
    {
        try
        {
            return Load(reg, guid);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// DeviceAPOInfo::load: null when the device is not present.
    internal static DeviceApo? Load(IApoRegistry reg, string deviceGuid)
    {
        var input = !reg.KeyExists($@"{ApoIds.RenderKey}\{deviceGuid}");
        var d = new DeviceApo(deviceGuid, input);
        var state = reg.ReadDword(d.KeyPath, ApoIds.DeviceStateValue) ?? 0;
        if ((state & ApoIds.StateNotPresent) != 0) return null;
        d.Disabled = (state & ApoIds.StateDisabled) != 0 || (state & ApoIds.StateDisabledInRegistry) != 0;
        d.Unplugged = (state & ApoIds.StateUnplugged) != 0;
        var properties = d.KeyPath + @"\Properties";
        d.Connection = reg.ReadString(properties, ApoIds.ConnectionNameValue) ?? string.Empty;
        d.Name = reg.ReadString(properties, ApoIds.DeviceNameValue) ?? string.Empty;
        d.EnhancementsOff = reg.KeyExists(d.FxKey) && reg.ReadDword(d.FxKey, ApoIds.DisableEnhancements) is { } off && off != 0;

        var mode = ApoInstallMode.LfxGfx;
        bool preMix = true, postMix = !input, originalPre = true, originalPost = !input, silent = false, auto = true;

        if (!reg.KeyExists(d.FxKey))
        {
            Array.Fill(d.Original, ApoIds.NoKey);
        }
        else
        {
            for (var i = 0; i < ApoIds.FxSlots.Count; i++)
            {
                var value = reg.ReadString(d.FxKey, ApoIds.FxSlots[i]);
                d.Original[i] = value is null || ApoIds.IsUpstream(value) ? ApoIds.NoValue : value;
            }

            var foundAt = ApoIds.FxSlots.Select(slot => ApoIds.IsUpstream(reg.ReadString(d.FxKey, slot))).ToArray();
            if (foundAt.Any(f => f))
            {
                d.Installed = true;
                if (reg.KeyExists(d.ChildKey))
                {
                    var version = reg.ReadString(d.ChildKey, ApoIds.InstallVersionValue);
                    if (version is not null && version != ApoIds.InstallVersion)
                        throw new InvalidDataException("A newer Equalizer APO attached this device; it is left as it is.");
                    d.Version = version ?? "1";
                    for (var i = 0; i < ApoIds.FxSlots.Count; i++)
                        if (reg.ReadString(d.ChildKey, ApoIds.FxSlots[i]) is { } kept) d.Original[i] = kept;

                    if (d.Version == ApoIds.InstallVersion)
                    {
                        d.PreMixChildGuid = reg.ReadString(d.ChildKey, ApoIds.PreMixChild) ?? string.Empty;
                        d.PostMixChildGuid = reg.ReadString(d.ChildKey, ApoIds.PostMixChild) ?? string.Empty;
                        preMix = foundAt[Lfx] || foundAt[Sfx];
                        postMix = foundAt[Gfx] || foundAt[Mfx] || foundAt[Efx];
                        if (d.PreMixChildGuid.Length == 0) originalPre = false;
                        if (d.PostMixChildGuid.Length == 0) originalPost = false;
                        if (foundAt[Lfx] || foundAt[Gfx]) mode = ApoInstallMode.LfxGfx;
                        else if (foundAt[Efx]) mode = ApoInstallMode.SfxEfx;
                        else if (foundAt[Sfx] || foundAt[Mfx]) mode = ApoInstallMode.SfxMfx;
                        if (reg.ReadString(d.ChildKey, ApoIds.AllowSilentBuffer) is { } allow) silent = allow != "false";
                        if (reg.ReadString(d.ChildKey, ApoIds.DisableAutoAdjust) is { } disable) auto = disable == "false";
                    }
                    else if (reg.ValueExists(d.ChildKey, ApoIds.Lfx))
                    {
                        (d.PreMixChildGuid, d.PostMixChildGuid) = (d.Original[Lfx], d.Original[Gfx]);
                    }
                    else
                    {
                        (d.PreMixChildGuid, d.PostMixChildGuid) = (d.Original[Sfx], d.Original[Mfx]);
                    }
                }
                else if (reg.ReadString(ApoIds.ChildApoKey, deviceGuid) is { } child)
                {
                    var at = Array.IndexOf(foundAt, true);
                    d.Original[at] = child;
                    if (at is Lfx or Sfx) d.PreMixChildGuid = child;
                    else d.PostMixChildGuid = child;
                }
            }
            else
            {
                // Only LFX/GFX when the driver brought only those; a combined Bluetooth device can't take EFX; else SFX/EFX.
                var onlyLfxGfx = (reg.ValueExists(d.FxKey, ApoIds.Lfx) || reg.ValueExists(d.FxKey, ApoIds.Gfx))
                    && !new[] { ApoIds.Sfx, ApoIds.Mfx, ApoIds.Efx }.Concat(ApoIds.MultiFxSlots).Any(slot => reg.ValueExists(d.FxKey, slot));
                mode = onlyLfxGfx ? ApoInstallMode.LfxGfx
                    : reg.ValueExists(properties, ApoIds.CombinedDeviceValue) ? ApoInstallMode.SfxMfx
                    : ApoInstallMode.SfxEfx;
            }
        }
        d.Current = new ApoInstallState(mode, preMix, postMix, originalPre, originalPost, silent, auto);
        return d;
    }

    /// The device's own pre-mix APO for <paramref name="mode"/> ("" when it has none): getOriginalAPOPreMix.
    public string OriginalPreMix(ApoInstallMode mode) => Kept(mode switch
    {
        ApoInstallMode.LfxGfx => Original[Lfx] == ApoIds.NoValue && Original[Gfx] == ApoIds.NoValue ? Original[Sfx] : Original[Lfx],
        _ => Original[Sfx] == ApoIds.NoValue && Original[mode == ApoInstallMode.SfxMfx ? Mfx : Efx] == ApoIds.NoValue ? Original[Lfx] : Original[Sfx],
    });

    /// The device's own post-mix APO for <paramref name="mode"/> ("" when it has none): getOriginalAPOPostMix.
    public string OriginalPostMix(ApoInstallMode mode) => Kept(mode switch
    {
        ApoInstallMode.LfxGfx => Original[Lfx] == ApoIds.NoValue && Original[Gfx] == ApoIds.NoValue ? Original[Mfx] : Original[Gfx],
        ApoInstallMode.SfxMfx => Original[Sfx] == ApoIds.NoValue && Original[Mfx] == ApoIds.NoValue ? Original[Gfx] : Original[Mfx],
        _ => Original[Sfx] == ApoIds.NoValue && Original[Efx] == ApoIds.NoValue ? Original[Gfx] : Original[Efx],
    });

    private static string Kept(string? guid) => guid is null or ApoIds.NoKey or ApoIds.NoValue ? string.Empty : guid;

    /// DeviceAPOInfo::install with <paramref name="s"/> as the selected state. Returns the backup of the slots that were there (Device
    /// Selector's .reg file text), or null when there was nothing to back up.
    internal string? Install(IApoRegistry reg, ApoInstallState s)
    {
        if (!s.PreMix && !s.PostMix) return null;
        reg.CreateKey(ApoIds.ChildApoKey);
        reg.CreateKey(ChildKey);
        string? backup = null;
        if (!reg.KeyExists(FxKey))
        {
            try
            {
                reg.CreateKey(FxKey);
            }
            catch (Exception ex) when (MachineRegistry.IsRegistryFailure(ex))
            {
                reg.MakeWritable(KeyPath); // the driver's key may not let administrators add to it
                reg.CreateKey(FxKey);
            }
            reg.WriteString(FxKey, ApoIds.FxTitleValue, "Equalizer APO");
            foreach (var slot in ApoIds.FxSlots) reg.WriteString(ChildKey, slot, ApoIds.NoKey);
        }
        else
        {
            var present = new List<(string Name, string Value)>();
            foreach (var slot in ApoIds.FxSlots)
            {
                var value = reg.ReadString(FxKey, slot);
                if (value is not null) present.Add((slot, value));
                reg.WriteString(ChildKey, slot, value ?? ApoIds.NoValue);
            }
            if (present.Count > 0) backup = BackupText(FxKey, present);
        }

        reg.WriteString(ChildKey, ApoIds.PreMixChild, s.OriginalPreMix ? OriginalPreMix(s.Mode) : string.Empty);
        reg.WriteString(ChildKey, ApoIds.PostMixChild, s.OriginalPostMix ? OriginalPostMix(s.Mode) : string.Empty);
        reg.WriteString(ChildKey, ApoIds.AllowSilentBuffer, s.AllowSilentBuffer ? "true" : "false");
        if (s.AutoAdjust) reg.DeleteValue(ChildKey, ApoIds.DisableAutoAdjust);
        else reg.WriteString(ChildKey, ApoIds.DisableAutoAdjust, "true");
        reg.WriteString(ChildKey, ApoIds.InstallVersionValue, ApoIds.InstallVersion);

        var postMix = s.PostMix && !Input;
        switch (s.Mode)
        {
            case ApoInstallMode.LfxGfx:
                if (s.PreMix) reg.WriteString(FxKey, ApoIds.Lfx, ApoIds.UpstreamPreMixClsid);
                if (postMix) reg.WriteString(FxKey, ApoIds.Gfx, ApoIds.UpstreamPostMixClsid);
                foreach (var slot in new[] { ApoIds.Sfx, ApoIds.Mfx, ApoIds.Efx }) reg.DeleteValue(FxKey, slot);
                break;
            case ApoInstallMode.SfxMfx:
                reg.DeleteValue(FxKey, ApoIds.Lfx);
                reg.DeleteValue(FxKey, ApoIds.Gfx);
                if (s.PreMix) Put(reg, ApoIds.Sfx, ApoIds.UpstreamPreMixClsid, ApoIds.SfxModes);
                if (postMix) Put(reg, ApoIds.Mfx, ApoIds.UpstreamPostMixClsid, ApoIds.MfxModes);
                break; // EFX is left as it is
            case ApoInstallMode.SfxEfx:
                reg.DeleteValue(FxKey, ApoIds.Lfx);
                reg.DeleteValue(FxKey, ApoIds.Gfx);
                if (s.PreMix) Put(reg, ApoIds.Sfx, ApoIds.UpstreamPreMixClsid, ApoIds.SfxModes);
                if (postMix) Put(reg, ApoIds.Efx, ApoIds.UpstreamPostMixClsid, ApoIds.EfxModes);
                break; // MFX is left as it is
        }
        reg.DeleteValue(FxKey, ApoIds.DisableEnhancements); // enhancements are switched on, or Windows loads no APO
        return backup;
    }

    private void Put(IApoRegistry reg, string slot, string clsid, string modes)
    {
        reg.WriteString(FxKey, slot, clsid);
        if (!reg.ValueExists(FxKey, modes)) reg.WriteMulti(FxKey, modes, ApoIds.DefaultProcessingMode);
    }

    /// DeviceAPOInfo::uninstall: the slots go back to what they were (the key is deleted when the device had none), and its record goes.
    internal void Uninstall(IApoRegistry reg)
    {
        if (Original[0] == ApoIds.NoKey)
        {
            reg.DeleteKey(FxKey);
        }
        else
        {
            for (var i = 0; i < ApoIds.FxSlots.Count; i++)
            {
                if (Original[i] == ApoIds.NoValue) reg.DeleteValue(FxKey, ApoIds.FxSlots[i]);
                else if (Original[i].Length > 0) reg.WriteString(FxKey, ApoIds.FxSlots[i], Original[i]);
            }
        }
        if (reg.KeyExists(ApoIds.ChildApoKey)) reg.DeleteValue(ApoIds.ChildApoKey, Guid);
        if (reg.KeyExists(ChildKey)) reg.DeleteKey(ChildKey);
        if (reg.KeyExists(ApoIds.ChildApoKey) && reg.KeyEmpty(ApoIds.ChildApoKey)) reg.DeleteKey(ApoIds.ChildApoKey);
    }

    /// RegistryHelper::saveToFile's text: the slots that were there, as a .reg file.
    internal static string BackupText(string key, IEnumerable<(string Name, string Value)> values) =>
        $"Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\{key}]\n" + string.Concat(values.Select(v => $"\"{v.Name}\"=\"{v.Value}\"\n")) + "\n";

    /// The name of Device Selector's backup file for this device ("backup_<name>_<connection>.reg", unsafe characters replaced).
    internal string BackupFileName => $"backup_{Safe(Name)}_{Safe(Connection)}.reg";

    private static string Safe(string text) => string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
