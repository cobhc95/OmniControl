using OmniControl.App.Features.Devices;

namespace OmniControl.Tests;

public sealed class DevicesTests
{
    [Fact]
    public void A_kasa_command_never_carries_the_account()
    {
        foreach (var profile in ScriptProfiles.BuiltIn)
            foreach (var template in new[] { profile.On, profile.Off, profile.State })
            {
                var arguments = KasaCommand.Fill(template, "192.168.0.5", "a@b.c");
                Assert.Equal(KasaCommand.Program, arguments[0]);
                Assert.Contains("192.168.0.5", arguments);
                Assert.DoesNotContain(arguments, a => a.Contains("a@b.c", StringComparison.OrdinalIgnoreCase) || a.Contains("password", StringComparison.OrdinalIgnoreCase));
            }
        Assert.Equal(ScriptProfiles.Auto, ScriptProfiles.Of("deleted", Array.Empty<ScriptProfile>()).Id);
    }

    [Theory]
    [InlineData("kasa --host {ip} on", true)]
    [InlineData("KASA --host {ip} on", true)]
    [InlineData("\"kasa\" on", true)]
    [InlineData("powershell -c x", false)]
    [InlineData("C:\\tools\\kasa.exe on", false)]
    [InlineData("", false)]
    public void Only_a_command_that_starts_with_kasa_is_given_the_account(string template, bool expected) =>
        Assert.Equal(expected, KasaCommand.RunsKasa(template));

    [Fact]
    public void Tapo_and_Kasa_get_the_email_in_lower_case_and_the_field_is_left_alone()
    {
        Assert.Equal("a.b@c.com", KasaCommand.EmailFor("  A.B@C.com ", ScriptProfiles.Of(ScriptProfiles.Tapo, Array.Empty<ScriptProfile>()).LowerCaseEmail));
        Assert.True(ScriptProfiles.Of(ScriptProfiles.Kasa, Array.Empty<ScriptProfile>()).LowerCaseEmail);
        Assert.Equal("A@B.c", KasaCommand.EmailFor(" A@B.c ", false));
    }

    [Fact]
    public void Kasas_problem_line_is_the_error_at_the_end_of_its_output()
    {
        Assert.Equal("kasa.exceptions.AuthenticationError: Device response did not match", KasaCommand.ProblemLine("Traceback (most recent call last):\n  File \"x\"\nkasa.exceptions.AuthenticationError: Device response did not match\n"));
        Assert.Equal("No device found", KasaCommand.ProblemLine("Discovering\nNo device found\n"));
        Assert.Equal(string.Empty, KasaCommand.ProblemLine(""));
    }

    [Fact]
    public void The_earlier_tapo_and_kasa_profiles_follow_their_replacements()
    {
        Assert.Equal(ScriptProfiles.Tapo, ScriptProfiles.Of("tapo-plug", Array.Empty<ScriptProfile>()).Id);
        Assert.Equal(ScriptProfiles.Tapo, ScriptProfiles.Of("tapo-bulb", Array.Empty<ScriptProfile>()).Id);
        Assert.Equal(ScriptProfiles.Kasa, ScriptProfiles.Of("kasa-plug", Array.Empty<ScriptProfile>()).Id);
    }

    [Fact]
    public void Kasas_noise_lines_are_dropped_and_the_raised_error_is_shown()
    {
        const string printed = "Discovering device\nRaised error: Timed out getting discovery response\nRun with --debug to see the traceback\nUnclosed client session\nclient_session: <aiohttp.ClientSession>";
        Assert.Equal("Raised error: Timed out getting discovery response", KasaCommand.ProblemLine(printed));
    }

    [Fact]
    public void Saved_devices_hold_no_password_and_are_backed_up()
    {
        var json = JsonSerializer.Serialize(new DeviceSettings { Devices = { new DeviceRecord { Name = "Desk plug", Methods = { new MethodRecord { Address = "192.168.0.5", Email = "a@b.c" } } } } });
        Assert.DoesNotContain("assword", json);
        Assert.Contains(DataStores.BackedUp, s => s == DataStores.Devices);
    }

    [Fact]
    public void An_unknown_method_falls_back_to_scripts() => Assert.Equal(DeviceMethods.Scripts, DeviceMethods.Known("Teleport"));

    [Fact]
    public void The_password_lives_under_a_name_of_its_own_per_method()
    {
        var a = new MethodRecord();
        var b = new MethodRecord();
        Assert.NotEqual(DeviceService.PasswordTarget(a), DeviceService.PasswordTarget(b));
    }

    [Fact]
    public void A_device_with_no_method_gets_one_with_its_id()
    {
        var device = new DeviceRecord { Id = "d" };
        Assert.True(device.EnsureMethod());
        var method = Assert.Single(device.Methods);
        Assert.Equal(DeviceMethods.Scripts, method.Kind);
        Assert.Equal("d", method.Id); // the password (kept by id) still fits
        Assert.False(device.EnsureMethod());
    }

    [Fact]
    public void Methods_of_the_same_kind_are_numbered_by_their_order()
    {
        var first = new MethodRecord();
        var sim = new MethodRecord { Kind = DeviceMethods.Simulation };
        var second = new MethodRecord();
        var device = new DeviceRecord { Methods = { first, sim, second } };
        Assert.Equal("Scripts 1", device.NameOf(first));
        Assert.Equal("Simulation", device.NameOf(sim));
        Assert.Equal("Scripts 2", device.NameOf(second));
        Assert.False(device.IsReady);
        sim.SimVerified = true;
        Assert.False(device.IsReady); // not taught yet
        second.Verified = true;
        Assert.True(device.IsReady);
    }

    [Fact]
    public void A_copy_of_a_device_keeps_its_methods_apart_from_the_original()
    {
        var device = new DeviceRecord { Methods = { new MethodRecord { Address = "1.2.3.4" } } };
        var before = device.Clone();
        device.Methods[0].Address = "5.6.7.8";
        device.Methods.Add(new MethodRecord());
        device.CopyFrom(before);
        Assert.Equal("1.2.3.4", Assert.Single(device.Methods).Address);
    }
}
