using OmniControl.App.Features.Devices;

namespace OmniControl.Tests;

/// The device auto-check rules: "Same for every device", and Scripts-only checks (no UI, no devices).
public class AutoCheckTests
{
    private static DeviceRecord Plug(AutoCheck? own = null, string kind = DeviceMethods.Scripts) =>
        new() { AutoCheck = own, Methods = { new MethodRecord { Kind = kind } } };

    private static DeviceSettings With(params DeviceRecord[] devices) => new() { Devices = devices.ToList() };

    [Fact]
    public void Old_numbers_keep_their_meaning()
    {
        Assert.Equal(AutoChecks.Default, new DeviceSettings().AutoCheck);
        Assert.Equal(AutoCheck.StartAndEvery5, (AutoCheck)2);
        Assert.Equal(AutoCheck.StartAndEvery15, (AutoCheck)3);
    }

    [Fact]
    public void Same_for_every_device_is_on_by_default_and_a_pick_applies_to_all()
    {
        var a = Plug();
        var b = Plug();
        var settings = With(a, b);
        Assert.True(settings.AutoCheckSame);
        AutoChecks.Pick(settings, a, AutoCheck.StartOnly);
        Assert.Equal(AutoCheck.StartOnly, AutoChecks.Effective(settings, a));
        Assert.Equal(AutoCheck.StartOnly, AutoChecks.Effective(settings, b));
    }

    [Fact]
    public void With_the_switch_off_a_pick_changes_one_device_only()
    {
        var a = Plug();
        var b = Plug();
        var settings = With(a, b);
        AutoChecks.SetSame(settings, a, false);
        AutoChecks.Pick(settings, a, AutoCheck.Off);
        Assert.Equal(AutoCheck.Off, AutoChecks.Effective(settings, a));
        Assert.Equal(AutoChecks.Default, AutoChecks.Effective(settings, b));
    }

    [Fact]
    public void Reading_when_the_tray_opens_is_on_by_default_follows_the_switch_and_never_uses_simulation()
    {
        var a = Plug();
        var b = Plug();
        var sim = Plug(kind: DeviceMethods.Simulation);
        var settings = With(a, b, sim);
        Assert.True(AutoChecks.OnTray(settings, a));
        Assert.False(AutoChecks.OnTray(settings, sim));
        AutoChecks.PickOnTray(settings, a, false);
        Assert.False(AutoChecks.OnTray(settings, b));
        AutoChecks.SetSame(settings, a, false);
        AutoChecks.PickOnTray(settings, b, true);
        Assert.False(AutoChecks.OnTray(settings, a));
        Assert.True(AutoChecks.OnTray(settings, b));
    }

    [Fact]
    public void Turning_the_switch_on_makes_this_devices_choice_the_one_for_all()
    {
        var a = Plug();
        var b = Plug();
        var settings = With(a, b);
        AutoChecks.SetSame(settings, a, false);
        AutoChecks.Pick(settings, b, AutoCheck.WhenOpens);
        AutoChecks.SetSame(settings, b, true);
        Assert.Equal(AutoCheck.WhenOpens, AutoChecks.Effective(settings, a));
    }

    [Fact]
    public void A_simulation_only_device_is_manual_only()
    {
        var sim = Plug(kind: DeviceMethods.Simulation);
        var settings = With(sim);
        Assert.Null(AutoChecks.Effective(settings, sim));
        sim.Methods.Add(new MethodRecord { Kind = DeviceMethods.Scripts });
        Assert.NotNull(AutoChecks.Effective(settings, sim));
    }
}
