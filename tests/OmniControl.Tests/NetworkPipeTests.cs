using System.Text;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Tests;

/// The network service's protocol (<see cref="NetworkPipe"/>): request checks, framing and the client's differences. Nothing touches Windows;
/// "is a program there" is a fake.
public class NetworkPipeTests
{
    private const string Windows = @"C:\Windows";
    private const string Edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

    private static (NetRequest? Request, string? Refused) Parse(string json, bool exists = true) =>
        NetworkPipe.Parse(Encoding.UTF8.GetBytes(json), _ => exists, Windows);

    private static string Quote(string path) => JsonSerializer.Serialize(path);

    [Fact]
    public void SetUpload_ToAProgram_IsTakenWithItsPathMadeWhole()
    {
        var (request, refused) = Parse($$"""{"V":1,"Op":"SetUpload","Path":{{Quote(@"C:\Apps\Tools\..\game.exe")}},"Kbps":512}""");
        Assert.Null(refused);
        Assert.Equal(new NetRequest(1, NetOp.SetUpload, @"C:\Apps\game.exe", 512), request);
    }

    [Fact]
    public void Status_WithoutPath_IsTaken() => Assert.Equal(NetOp.Status, Parse("""{"V":1,"Op":"Status"}""").Request?.Op);

    [Theory]
    [InlineData("""{"V":2,"Op":"Status"}""")] // another protocol version
    [InlineData("""{"Op":"Status"}""")] // no version
    [InlineData("""{"V":1,"Op":"RunCommand"}""")] // not a request of the list
    [InlineData("""{"V":1,"Op":0}""")] // requests are named, never numbered
    [InlineData("""{"V":1,"Op":"Status","Command":"cmd.exe"}""")] // an unknown field
    [InlineData("""{"V":1,"Op":"Status","Path":"C:\\a.exe"}""")] // a path where none is taken
    [InlineData("""{"V":1,"Op":"Block","Path":"C:\\a.exe","Kbps":5}""")] // a limit where none is taken
    [InlineData("""{"V":1,"Op":"SetUpload","Path":"C:\\a.exe","Kbps":0}""")] // no limit
    [InlineData("""{"V":1,"Op":"SetDownload","Path":"C:\\a.exe","Kbps":1048577}""")] // above the highest limit
    [InlineData("""{"V":1,"Op":"SetUpload","Kbps":64}""")] // no path
    [InlineData("""[1,2]""")]
    [InlineData("""not json""")]
    public void BadRequests_AreRefused(string json)
    {
        var (request, refused) = Parse(json);
        Assert.Null(request);
        Assert.False(string.IsNullOrWhiteSpace(refused));
    }

    [Theory]
    [InlineData(@"game.exe")] // relative
    [InlineData(@"\\server\share\game.exe")] // UNC
    [InlineData(@"\\?\C:\game.exe")] // device path
    [InlineData(@"C:game.exe")] // drive-relative
    [InlineData(@"C:\Apps\game.exe:hidden")] // alternate data stream
    [InlineData(@"C:\Apps\*.exe")] // wildcard
    [InlineData(@"C:\Apps/game.exe")] // mixed separators
    [InlineData(@"C:\Apps\game.bat")] // not a program
    [InlineData(@"C:\Apps\game.exe" + "\n")] // control character
    [InlineData(@"C:\Windows\System32\svchost.exe")] // Windows' own program
    [InlineData(@"c:\windows\..\Windows\explorer.exe")] // Windows' own program, written around
    public void BadPaths_AreRefused(string path)
    {
        var (request, refused) = Parse($$"""{"V":1,"Op":"Block","Path":{{Quote(path)}}}""");
        Assert.Null(request);
        Assert.NotNull(refused);
    }

    [Fact]
    public void TooLongPath_IsRefused() =>
        Assert.Null(Parse($$"""{"V":1,"Op":"Block","Path":{{Quote(@"C:\" + new string('a', NetworkPipe.MaxPath) + ".exe")}}}""").Request);

    [Fact]
    public void SetForAMissingProgram_IsRefused_ButClearIsTaken()
    {
        Assert.Null(Parse($$"""{"V":1,"Op":"SetDownload","Path":{{Quote(Edge)}},"Kbps":64}""", exists: false).Request);
        Assert.Equal(NetOp.ClearDownload, Parse($$"""{"V":1,"Op":"ClearDownload","Path":{{Quote(Edge)}}}""", exists: false).Request?.Op);
    }

    [Fact]
    public async Task Message_GoesThroughTheFrameUnchanged()
    {
        var reply = new NetReply(NetworkPipe.Version, Problems: new[] { "one" }, Rules: new[] { new NetRule(Edge, 64, 0, true) },
            Speeds: new[] { new AppTraffic("msedge", Edge, 10, 20) });
        using var stream = new MemoryStream();
        await NetworkPipe.WriteAsync(stream, NetworkPipe.Encode(reply), NetworkPipe.MaxReply, CancellationToken.None);
        stream.Position = 0;
        var read = NetworkPipe.ReadReply((await NetworkPipe.ReadAsync(stream, NetworkPipe.MaxReply, CancellationToken.None))!);
        Assert.NotNull(read);
        Assert.Equal(reply.Problems, read.Problems);
        Assert.Equal(reply.Rules, read.Rules);
        Assert.Equal(reply.Speeds, read.Speeds);
        Assert.Null(await NetworkPipe.ReadAsync(stream, NetworkPipe.MaxReply, CancellationToken.None)); // the other side closed
    }

    [Fact]
    public async Task TooLargeMessage_IsNeitherWrittenNorRead()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => NetworkPipe.WriteAsync(stream, new byte[NetworkPipe.MaxRequest + 1], NetworkPipe.MaxRequest, CancellationToken.None));
        stream.Write(BitConverter.GetBytes(NetworkPipe.MaxRequest + 1));
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => NetworkPipe.ReadAsync(stream, NetworkPipe.MaxRequest, CancellationToken.None));
    }

    [Fact]
    public async Task EmptyOrCutMessage_IsRefused()
    {
        using var empty = new MemoryStream(BitConverter.GetBytes(0));
        await Assert.ThrowsAsync<InvalidDataException>(() => NetworkPipe.ReadAsync(empty, NetworkPipe.MaxRequest, CancellationToken.None));
        using var cut = new MemoryStream(BitConverter.GetBytes(10).Concat(new byte[] { 1, 2 }).ToArray());
        await Assert.ThrowsAsync<EndOfStreamException>(() => NetworkPipe.ReadAsync(cut, NetworkPipe.MaxRequest, CancellationToken.None));
    }

    [Fact]
    public void Changes_AskOnlyForTheDifferences()
    {
        var held = new NetRule(Edge, UploadKbps: 64, Blocked: true);
        var wanted = new NetRule(Edge, UploadKbps: 64, DownloadKbps: 128);
        Assert.Equal(new[]
        {
            new NetRequest(NetworkPipe.Version, NetOp.SetDownload, Edge, 128),
            new NetRequest(NetworkPipe.Version, NetOp.Unblock, Edge),
        }, NetworkPipe.Changes(Edge, held, wanted));
        Assert.Empty(NetworkPipe.Changes(Edge, held, held));
        Assert.Equal(new[]
        {
            new NetRequest(NetworkPipe.Version, NetOp.ClearUpload, Edge),
            new NetRequest(NetworkPipe.Version, NetOp.Unblock, Edge),
        }, NetworkPipe.Changes(Edge, held, null));
    }

    [Fact]
    public void Rule_KeepsItsLimitsBothWays()
    {
        var rule = new NetworkRule("msedge", Edge, 512UL * 1024 * 8, true, 256L * 1024);
        var sent = NetRule.From(rule);
        Assert.Equal(new NetRule(Edge, 512, 256, true), sent);
        Assert.Equal(rule, sent.ToRule());
    }
}
