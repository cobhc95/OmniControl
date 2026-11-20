using OmniControl.Core.Engine.Resources;

namespace OmniControl.Tests;

/// Where the network service may run from (<see cref="NetworkServiceSetup.CanServeFrom"/>): read from the folders' access lists only.
public class NetworkServiceFolderTests
{
    [Fact]
    public void AFolderTheUserMayWrite_IsRefused() =>
        Assert.False(NetworkServiceSetup.CanServeFrom(Path.Combine(Path.GetTempPath(), "OmniControl.exe")));

    [Fact]
    public void WindowsOwnFolder_IsAdminOnly() =>
        Assert.True(NetworkServiceSetup.CanServeFrom(Path.Combine(Environment.SystemDirectory, "cmd.exe")));
}
