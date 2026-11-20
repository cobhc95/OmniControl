using OmniControl.Core.Engine.Apo;

namespace OmniControl.Tests;

/// OmniControl's own Equalizer APO, headless: who owns Equalizer APO for each set of signs found (and which leftovers may go), and the
/// ported Device Selector logic putting it on a device and taking it off again on an in-memory registry (nothing on the PC is read or
/// written): the original slots are kept under Child APOs and come back exactly.
public class BundledApoTests
{
    private const string Own = @"C:\Program Files\OmniControl\EqualizerAPO", Theirs = @"C:\Program Files\EqualizerAPO";
    private const string Guid = "{11111111-2222-3333-4444-555555555555}";
    private const string DriverLfx = "{AAAAAAAA-0000-0000-0000-000000000001}", DriverGfx = "{AAAAAAAA-0000-0000-0000-000000000002}";
    private const string DriverSfx = "{BBBBBBBB-0000-0000-0000-000000000001}", DriverMfx = "{BBBBBBBB-0000-0000-0000-000000000002}",
        DriverEfx = "{BBBBBBBB-0000-0000-0000-000000000003}";

    private static ApoOwnership Decide(bool uninstall = false, bool appKey = false, string? separate = null, string? dll = null, bool dllExists = true, string? marker = null) =>
        ApoOwnership.Decide(new ApoInstallFacts(uninstall, appKey, separate, dll, dll is not null && dllExists, Own, marker));

    [Fact]
    public void NothingInstalled_IsNone()
    {
        Assert.Equal(ApoOwnership.Nothing, Decide());
    }

    [Fact]
    public void OwnCopy_IsOurs()
    {
        Assert.Equal(new ApoOwnership(ApoOwner.Ours, false, ApoCleanup.None), Decide(appKey: true, dll: Own + @"\EqualizerAPO.dll", marker: Own + @"\"));
    }

    [Fact]
    public void SeparateInstall_IsNeverTouched()
    {
        Assert.Equal(new ApoOwnership(ApoOwner.Separate, false, ApoCleanup.None),
            Decide(uninstall: true, appKey: true, separate: Theirs, dll: Theirs + @"\EqualizerAPO.dll"));
        // A registered DLL elsewhere is a separate install even without its Uninstall entry.
        Assert.Equal(ApoOwner.Separate, Decide(dll: @"D:\Tools\EqualizerAPO.dll").Owner);
    }

    [Fact]
    public void TheirsInstalledOverOurs_SwitchesToTheirsAndCleansOurLeftovers()
    {
        Assert.Equal(new ApoOwnership(ApoOwner.Separate, false, ApoCleanup.FilesAndMarker),
            Decide(uninstall: true, appKey: true, separate: Theirs, dll: Theirs + @"\EqualizerAPO.dll", marker: Own));
    }

    [Fact]
    public void SeparateInstallInOurFolder_TakesOnlyTheMarker()
    {
        Assert.Equal(ApoCleanup.MarkerOnly, Decide(uninstall: true, separate: Own, dll: Own + @"\EqualizerAPO.dll", marker: Own).Cleanup);
    }

    [Fact]
    public void TheirsUninstalled_SaysSoAndOffersOurs()
    {
        Assert.Equal(new ApoOwnership(ApoOwner.None, true, ApoCleanup.None), Decide(appKey: true));
        Assert.True(Decide(appKey: true, dll: Theirs + @"\EqualizerAPO.dll", dllExists: false).SeparateRemoved);
    }

    [Fact]
    public void OwnCopyBroken_IsNoneAndCleaned()
    {
        Assert.Equal(new ApoOwnership(ApoOwner.None, false, ApoCleanup.FilesAndMarker), Decide(dll: Own + @"\EqualizerAPO.dll", dllExists: false, marker: Own));
        Assert.Equal(ApoCleanup.FilesAndMarker, Decide(marker: Own).Cleanup);
    }

    [Fact]
    public void OurPathWithoutMarker_IsNotOurs()
    {
        Assert.Equal(ApoOwner.None, Decide(dll: Own + @"\EqualizerAPO.dll").Owner);
    }

    private static string Render => $@"{ApoIds.RenderKey}\{Guid}";
    private static string Capture => $@"{ApoIds.CaptureKey}\{Guid}";
    private static string Fx(string device) => device + @"\FxProperties";
    private static string Child => $@"{ApoIds.ChildApoKey}\{Guid}";

    private static FakeRegistry Device(string key, Dictionary<string, object>? fx, Dictionary<string, object>? properties = null)
    {
        var reg = new FakeRegistry();
        reg.Set(key, ApoIds.DeviceStateValue, 1u);
        reg.Set(key + @"\Properties", ApoIds.DeviceNameValue, "Speakers");
        foreach (var (name, value) in properties ?? new()) reg.Set(key + @"\Properties", name, value);
        if (fx is not null)
        {
            reg.CreateKey(Fx(key));
            foreach (var (name, value) in fx) reg.Set(Fx(key), name, value);
        }
        return reg;
    }

    private static DeviceApo Load(FakeRegistry reg) => DeviceApo.Load(reg, Guid) ?? throw new InvalidOperationException("not present");

    [Fact]
    public void LfxGfxDriver_InstallsAsLfxGfx_KeepsOriginalsAndRestoresExactly()
    {
        var reg = Device(Render, new() { [ApoIds.Lfx] = DriverLfx, [ApoIds.Gfx] = DriverGfx, ["{other},1"] = 7 });
        var before = reg.Values(Fx(Render));
        var device = Load(reg);
        Assert.False(device.Installed);
        Assert.Equal(ApoInstallMode.LfxGfx, device.Current.Mode);

        var backup = device.Install(reg, device.Current);
        Assert.Equal(ApoIds.UpstreamPreMixClsid, reg.Get(Fx(Render), ApoIds.Lfx));
        Assert.Equal(ApoIds.UpstreamPostMixClsid, reg.Get(Fx(Render), ApoIds.Gfx));
        Assert.Equal(DriverLfx, reg.Get(Child, ApoIds.Lfx));
        Assert.Equal(DriverGfx, reg.Get(Child, ApoIds.Gfx));
        Assert.Equal(ApoIds.NoValue, reg.Get(Child, ApoIds.Sfx));
        Assert.Equal(DriverLfx, reg.Get(Child, ApoIds.PreMixChild));
        Assert.Equal(DriverGfx, reg.Get(Child, ApoIds.PostMixChild));
        Assert.Equal(ApoIds.InstallVersion, reg.Get(Child, ApoIds.InstallVersionValue));
        Assert.Contains(DriverLfx, backup);
        Assert.Contains(DriverGfx, backup);

        var installed = Load(reg);
        Assert.True(installed.Installed);
        Assert.Equal(new[] { DriverLfx, DriverGfx, ApoIds.NoValue, ApoIds.NoValue, ApoIds.NoValue }, installed.Original);
        Assert.Equal(device.Current, installed.Current);

        installed.Uninstall(reg);
        Assert.Equal(before, reg.Values(Fx(Render)));
        Assert.False(reg.KeyExists(ApoIds.ChildApoKey));
    }

    [Fact]
    public void SfxMfxEfxDriver_InstallsAsSfxEfx_TurnsEnhancementsOnAndRestoresTheSlots()
    {
        var reg = Device(Render, new()
        {
            [ApoIds.Sfx] = DriverSfx, [ApoIds.Mfx] = DriverMfx, [ApoIds.Efx] = DriverEfx, [ApoIds.SfxModes] = new[] { "{mode}" }, [ApoIds.DisableEnhancements] = 1u,
        });
        var device = Load(reg);
        Assert.True(device.EnhancementsOff);
        Assert.Equal(ApoInstallMode.SfxEfx, device.Current.Mode);

        device.Install(reg, device.Current);
        Assert.Equal(ApoIds.UpstreamPreMixClsid, reg.Get(Fx(Render), ApoIds.Sfx));
        Assert.Equal(DriverMfx, reg.Get(Fx(Render), ApoIds.Mfx)); // MFX is left as it is
        Assert.Equal(ApoIds.UpstreamPostMixClsid, reg.Get(Fx(Render), ApoIds.Efx));
        Assert.Equal(new[] { "{mode}" }, reg.Get(Fx(Render), ApoIds.SfxModes)); // modes that were there are kept
        Assert.Equal(new[] { ApoIds.DefaultProcessingMode }, reg.Get(Fx(Render), ApoIds.EfxModes));
        Assert.False(reg.ValueExists(Fx(Render), ApoIds.DisableEnhancements));
        Assert.Equal(DriverSfx, reg.Get(Child, ApoIds.PreMixChild));
        Assert.Equal(DriverEfx, reg.Get(Child, ApoIds.PostMixChild));

        Load(reg).Uninstall(reg);
        Assert.Equal(DriverSfx, reg.Get(Fx(Render), ApoIds.Sfx));
        Assert.Equal(DriverMfx, reg.Get(Fx(Render), ApoIds.Mfx));
        Assert.Equal(DriverEfx, reg.Get(Fx(Render), ApoIds.Efx));
        Assert.False(reg.ValueExists(Fx(Render), ApoIds.Lfx));
    }

    [Fact]
    public void CombinedBluetoothDevice_InstallsAsSfxMfx()
    {
        var reg = Device(Render, new() { [ApoIds.Sfx] = DriverSfx }, new() { [ApoIds.CombinedDeviceValue] = 1u });
        Assert.Equal(ApoInstallMode.SfxMfx, Load(reg).Current.Mode);
    }

    [Fact]
    public void DeviceWithoutFxProperties_GetsTheKeyAndLosesItAgain()
    {
        var reg = Device(Render, fx: null);
        reg.DenyFirstCreate = true;
        var device = Load(reg);
        Assert.Equal(Enumerable.Repeat(ApoIds.NoKey, 5), device.Original);

        Assert.Null(device.Install(reg, device.Current));
        Assert.True(reg.MadeWritable); // the driver's key refused the new key until administrators could write it
        Assert.Equal("Equalizer APO", reg.Get(Fx(Render), ApoIds.FxTitleValue));
        Assert.Equal(ApoIds.NoKey, reg.Get(Child, ApoIds.Lfx));
        Assert.Equal(string.Empty, reg.Get(Child, ApoIds.PreMixChild)); // no driver APO to run inside

        Load(reg).Uninstall(reg);
        Assert.False(reg.KeyExists(Fx(Render)));
        Assert.False(reg.KeyExists(ApoIds.ChildApoKey));
    }

    [Fact]
    public void LockedFxProperties_IsChangedAsItsOwnerAndRestored()
    {
        var reg = Device(Render, new() { [ApoIds.Sfx] = DriverSfx, [ApoIds.Efx] = DriverEfx });
        var before = reg.Values(Fx(Render));
        reg.FxLocked = true;
        var device = Load(reg);
        device.Install(reg, device.Current);
        Assert.True(reg.MadeWritable);
        Assert.Equal(ApoIds.UpstreamPreMixClsid, reg.Get(Fx(Render), ApoIds.Sfx));

        Load(reg).Uninstall(reg);
        Assert.Equal(DriverSfx, reg.Get(Fx(Render), ApoIds.Sfx));
        Assert.Equal(DriverEfx, reg.Get(Fx(Render), ApoIds.Efx));
        Assert.Equal(before.Keys.Where(k => k is not (ApoIds.SfxModes or ApoIds.EfxModes)), reg.Values(Fx(Render)).Keys.Where(k => k is not (ApoIds.SfxModes or ApoIds.EfxModes)));
    }

    [Fact]
    public void RefusedPartWay_PutsTheDeviceBackExactly()
    {
        var reg = Device(Render, new() { [ApoIds.Sfx] = DriverSfx, [ApoIds.Efx] = DriverEfx, [ApoIds.SfxModes] = new[] { "{mode}" }, [ApoIds.EfxModes] = new[] { "{mode}" } });
        var before = reg.Values(Fx(Render));
        reg.Refused = ApoIds.UpstreamPostMixClsid; // SFX is written, then EFX is refused
        var device = Load(reg);
        Assert.Throws<UnauthorizedAccessException>(() => device.Install(reg, device.Current));
        Assert.Equal(before, reg.Values(Fx(Render)));
        Assert.False(reg.KeyExists(ApoIds.ChildApoKey));
    }

    [Fact]
    public void Microphone_GetsOnlyThePreMixApo()
    {
        var reg = Device(Capture, new() { [ApoIds.Sfx] = DriverSfx });
        var device = Load(reg);
        Assert.True(device.Input);
        Assert.False(device.Current.PostMix);
        device.Install(reg, device.Current with { PostMix = true });
        Assert.Equal(ApoIds.UpstreamPreMixClsid, reg.Get(Fx(Capture), ApoIds.Sfx));
        Assert.False(reg.ValueExists(Fx(Capture), ApoIds.Efx));
        Assert.False(reg.ValueExists(Fx(Capture), ApoIds.Mfx));
    }

    [Fact]
    public void Reinstall_WithAnotherMode_StillRestoresTheOriginals()
    {
        var reg = Device(Render, new() { [ApoIds.Sfx] = DriverSfx, [ApoIds.Efx] = DriverEfx });
        var before = reg.Values(Fx(Render));
        var device = Load(reg);
        device.Install(reg, device.Current);
        var installed = Load(reg);
        installed.Uninstall(reg); // Device Selector's reinstall: back to the originals, read again, installed with the new options
        var again = Load(reg);
        again.Install(reg, again.Current with { Mode = ApoInstallMode.LfxGfx });
        Assert.Equal(ApoIds.UpstreamPreMixClsid, reg.Get(Fx(Render), ApoIds.Lfx));
        Assert.Equal(DriverSfx, reg.Get(Child, ApoIds.PreMixChild)); // LFX/GFX with no LFX/GFX original runs the SFX one inside

        Load(reg).Uninstall(reg);
        Assert.Equal(before.Where(v => v.Key is not (ApoIds.SfxModes or ApoIds.EfxModes)), reg.Values(Fx(Render)).Where(v => v.Key is not (ApoIds.SfxModes or ApoIds.EfxModes)));
    }

    [Fact]
    public void NotPresentDevice_IsLeftOut()
    {
        var reg = Device(Render, null);
        reg.Set(Render, ApoIds.DeviceStateValue, ApoIds.StateNotPresent);
        Assert.Null(DeviceApo.Load(reg, Guid));
    }

    [Fact]
    public void RemoteDevice_HasNoKeyAndIsNotTakenForAMicrophone()
    {
        var reg = Device(Render.Replace(@"\Render\", @"\RemoteRender\"), null);
        Assert.Null(DeviceApo.KeyOf(reg, Guid));
        Assert.Null(DeviceApo.Load(reg, Guid));
    }

    [Fact]
    public void InstallState_RoundTripsThroughTheHelperArguments()
    {
        var state = new ApoInstallState(ApoInstallMode.SfxMfx, true, false, true, false, true, false);
        Assert.Equal(state, ApoSetup.Decode(ApoSetup.Encode(state)));
        Assert.Null(ApoSetup.Decode("9111111"));
        Assert.EndsWith(ApoSetup.Encode(state), ApoSetup.Attach(Guid, state));
    }

    /// HKLM as dictionaries: keys (no case) to values.
    private sealed class FakeRegistry : IApoRegistry
    {
        private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

        public bool DenyFirstCreate { get; set; }
        /// FxProperties refuses every change until it is made as its owner.
        public bool FxLocked { get; set; }
        /// A value no write may put anywhere (a refusal part-way through an install).
        public string? Refused { get; set; }
        public bool MadeWritable { get; private set; }
        private bool _owning;

        public void Set(string key, string name, object value) => Open(key)[name] = value;

        private void Check(string key, object? value = null)
        {
            if (FxLocked && !_owning && key.EndsWith(@"\FxProperties", StringComparison.Ordinal) || value is not null && Equals(value, Refused))
                throw new UnauthorizedAccessException();
        }
        public object? Get(string key, string name) => _keys.TryGetValue(key, out var k) && k.TryGetValue(name, out var v) ? v : null;
        public SortedDictionary<string, string> Values(string key) =>
            new(_keys[key].ToDictionary(v => v.Key, v => v.Value is string[] m ? string.Join("|", m) : v.Value.ToString()!));

        private Dictionary<string, object> Open(string key)
        {
            if (!_keys.TryGetValue(key, out var k)) _keys[key] = k = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            return k;
        }

        public bool KeyExists(string key) => _keys.ContainsKey(key);
        public bool ValueExists(string key, string name) => Get(key, name) is not null;
        public string? ReadString(string key, string name) => Get(key, name) as string;
        public uint? ReadDword(string key, string name) => Get(key, name) is uint v ? v : null;
        public void WriteString(string key, string name, string value)
        {
            Check(key, value);
            Set(key, name, value);
        }

        public void WriteMulti(string key, string name, string value)
        {
            Check(key);
            Set(key, name, new[] { value });
        }

        public void DeleteValue(string key, string name)
        {
            Check(key);
            if (_keys.TryGetValue(key, out var k)) k.Remove(name);
        }

        public void CreateKey(string key)
        {
            if (DenyFirstCreate && key.EndsWith(@"\FxProperties", StringComparison.Ordinal) && !MadeWritable) throw new UnauthorizedAccessException();
            Open(key);
        }

        public void DeleteKey(string key) => _keys.Remove(key);
        public bool KeyEmpty(string key) => Open(key).Count == 0 && SubKeys(key).Count == 0;

        public IReadOnlyList<string> SubKeys(string key) => _keys.Keys
            .Where(k => k.StartsWith(key + @"\", StringComparison.OrdinalIgnoreCase) && !k[(key.Length + 1)..].Contains('\\'))
            .Select(k => k[(key.Length + 1)..]).ToList();

        public void AsOwner(string key, Action change)
        {
            MadeWritable = true;
            _owning = true;
            try
            {
                change();
            }
            finally
            {
                _owning = false;
            }
        }
    }
}
