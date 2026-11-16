using OmniControl.App.Automation;
using Xunit;

namespace OmniControl.Tests;

public class CustomFieldsTests
{
    [Fact]
    public void Every_target_setting_reads_and_writes_the_one_target()
    {
        var custom = new CustomAction { Name = "A" };
        Assert.True(CustomFields.Apply(custom, CustomFields.TargetProgram, @" C:\x.exe "));
        Assert.Equal(@"C:\x.exe", custom.Target);
        var values = CustomFields.Read(custom);
        Assert.Equal(custom.Target, values[CustomFields.TargetText]);
        Assert.Equal(custom.Target, values[CustomFields.TargetFile]);
    }

    [Fact]
    public void A_blank_name_is_not_kept()
    {
        var custom = new CustomAction { Name = "A" };
        Assert.False(CustomFields.Apply(custom, CustomFields.Name, "  "));
        Assert.Equal("A", custom.Name);
    }
}
