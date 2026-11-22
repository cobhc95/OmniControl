using System.Text.Json;
using Avalonia.Media;
using OmniControl.App.Features.Resources;
using OmniControl.App.Features.Resources.Gadget;

namespace OmniControl.Tests;

/// The gadget's Background setting: the old Solid background switch loads as the taskbar's colour, a layout keeps it, and each stored value fills as it says.
public class GadgetBackgroundTests
{
    [Theory]
    [InlineData("{\"SolidBackground\":true}", GadgetSettings.TaskbarBackground)]
    [InlineData("{\"SolidBackground\":false}", "")]
    [InlineData("{}", "")]
    public void OldSolidBackground_LoadsAsTaskbarColour(string json, string expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<GadgetSettings>(json)!.Background);

    [Fact]
    public void NewFiles_DoNotWriteTheOldSwitch() => Assert.DoesNotContain("SolidBackground", JsonSerializer.Serialize(new GadgetSettings()));

    [Fact]
    public void ALayout_KeepsTheBackground()
    {
        var look = new GadgetSettings { Background = GadgetSettings.TaskbarBackground };
        look.TakeLook(new GadgetSettings());
        Assert.Equal(GadgetSettings.TaskbarBackground, look.Background);
    }

    [Fact]
    public void FillOf_EachStoredValue()
    {
        var taskbar = Colors.Gray;
        Assert.Null(TaskbarGadget.FillOf(string.Empty, taskbar));
        Assert.Equal(taskbar, TaskbarGadget.FillOf(GadgetSettings.TaskbarBackground, taskbar));
        Assert.Equal(Colors.Red, TaskbarGadget.FillOf(Colors.Red.ToString(), taskbar));
        Assert.Null(TaskbarGadget.FillOf("not a colour", taskbar));
    }

    [Fact]
    public void Choices_NameEveryValueAndTheUsersOwn() => Assert.Equal(GadgetSettings.Backgrounds.Count + 1, GadgetSettings.BackgroundNames.Count);
}
