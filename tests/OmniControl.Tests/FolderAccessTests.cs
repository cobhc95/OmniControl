using System.Security.AccessControl;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Tests;

/// The "only administrators can change it" decision on made-up access lists (what an elevated plug-in load and the network service rely on).
public class FolderAccessTests
{
    private static RawSecurityDescriptor Sd(string sddl) => new(sddl);

    [Theory]
    [InlineData("O:BAD:(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)", true)] // Program Files style: users read only
    [InlineData("O:BAD:(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1301bf;;;AU)", false)] // signed-in users may modify
    [InlineData("O:BAD:(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x2;;;BU)", false)] // users may add files
    [InlineData("O:BUD:(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", false)] // owned by users
    [InlineData("O:BAD:(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;CO)", true)] // creator owner stands for an administrator here
    public void OwnFolder(string sddl, bool adminOnly) => Assert.Equal(adminOnly, NetworkServiceSetup.AdminOnly(Sd(sddl), own: true));

    [Fact]
    public void NoListLetsEveryoneIn() => Assert.False(NetworkServiceSetup.AdminOnly(Sd("O:BA"), own: true));

    [Fact]
    public void AboveTheFolderOnlyChangingItsChildrenCounts()
    {
        // Users may add files to C:\ (inherit-only entries apply to children, not the folder itself).
        const string root = "O:SYD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;BU)(A;CIIO;0x2;;;AU)(A;OICIIO;0x1301bf;;;AU)";
        Assert.True(NetworkServiceSetup.AdminOnly(Sd(root), own: false));
        Assert.False(NetworkServiceSetup.AdminOnly(Sd(root), own: true));
    }
}
