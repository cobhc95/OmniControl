using System.Globalization;
using System.Runtime.InteropServices;
using OmniControl.App.Features.Resources;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Tests;

/// The per-program traffic reader's event parsing (<see cref="NetworkByApp.TryRead"/>) on a hand-made x64 EVENT_RECORD, and the page's words
/// for a program's small speeds. No ETW session is started.
public class NetworkByAppTests
{
    private const int RecordSize = 112, EventId = 40, UserDataLength = 86, UserData = 96;

    private static (bool Read, int Pid, int Size, bool Sent) Parse(ushort eventId, int pid, int size, ushort length = 36)
    {
        IntPtr record = Marshal.AllocHGlobal(RecordSize), payload = Marshal.AllocHGlobal(64);
        try
        {
            Marshal.Copy(new byte[RecordSize], 0, record, RecordSize);
            Marshal.WriteInt16(record, EventId, unchecked((short)eventId));
            Marshal.WriteInt16(record, UserDataLength, unchecked((short)length));
            Marshal.WriteIntPtr(record, UserData, payload);
            Marshal.WriteInt32(payload, pid);
            Marshal.WriteInt32(payload, 4, size);
            var read = NetworkByApp.TryRead(record, out var p, out var s, out var sent);
            return (read, p, s, sent);
        }
        finally
        {
            Marshal.FreeHGlobal(payload);
            Marshal.FreeHGlobal(record);
        }
    }

    [Theory]
    [InlineData(10)] // TCP IPv4
    [InlineData(26)] // TCP IPv6
    [InlineData(42)] // UDP IPv4
    [InlineData(58)] // UDP IPv6
    public void SendEvents_AreSentBytesOfTheirProcess(ushort id) => Assert.Equal((true, 4321, 1460, true), Parse(id, 4321, 1460));

    [Theory]
    [InlineData(11)]
    [InlineData(27)]
    [InlineData(43)]
    [InlineData(59)]
    public void ReceiveEvents_AreReceivedBytesOfTheirProcess(ushort id) => Assert.Equal((true, 88, 65536, false), Parse(id, 88, 65536));

    [Theory]
    [InlineData(12)] // connect
    [InlineData(13)] // disconnect
    [InlineData(14)] // retransmit
    public void OtherEvents_AreSkipped(ushort id) => Assert.False(Parse(id, 1, 1).Read);

    [Fact]
    public void ShortPayload_IsSkipped() => Assert.False(Parse(10, 1, 1, length: 4).Read);

    [Fact]
    public void SmallSpeeds_DoNotReadAsZero()
    {
        Assert.Equal($"{0.4.ToString("F1", CultureInfo.CurrentCulture)} KB/s", Reading.SpeedWords(400));
        Assert.Equal("0 KB/s", Reading.SpeedWords(0));
        Assert.Equal("320 KB/s", Reading.SpeedWords(320 * 1024));
    }
}
