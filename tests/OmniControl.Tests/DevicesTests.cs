using System.Text.Json;
using OmniControl.App.Devices;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using Xunit;

namespace OmniControl.Tests;

public sealed class DevicesTests
{
    [Fact]
    public void A_kasa_command_never_carries_the_account()
    {
        var arguments = KasaCommand.Arguments("192.168.0.5", KasaCommand.PowerOff);
        Assert.Equal(new[] { "--host", "192.168.0.5", "--device-family", "SMART.TAPOPLUG", "--encrypt-type", "KLAP", "--login-version", "2", "off" }, arguments);
        Assert.DoesNotContain(arguments, a => a.Contains("username", StringComparison.OrdinalIgnoreCase) || a.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Saved_devices_hold_no_password_and_are_backed_up()
    {
        var json = JsonSerializer.Serialize(new DeviceSettings { Devices = { new DeviceRecord { Name = "Desk plug", Address = "192.168.0.5", Email = "a@b.c" } } });
        Assert.DoesNotContain("assword", json);
        Assert.Contains(DataStores.BackedUp, s => s == DataStores.Devices);
    }

    [Fact]
    public void An_unknown_method_falls_back_to_scripts() => Assert.Equal(DeviceMethods.Scripts, DeviceMethods.Known("Teleport"));

    [Fact]
    public void The_password_lives_under_a_name_of_its_own_per_device()
    {
        var a = new DeviceRecord();
        var b = new DeviceRecord();
        Assert.NotEqual(DeviceService.PasswordTarget(a), DeviceService.PasswordTarget(b));
    }
}
