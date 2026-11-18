using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace OmniControl.Core.Engine.Wake;

/// <summary>The Wake-on-LAN "magic packet": six 0xFF bytes and the card's address sixteen times, sent as a broadcast to the card's own network.</summary>
public static class MagicPacket
{
    private const int Port = 9, MacBytes = 6, Repeats = 16;

    /// <summary>The address "AA:BB:CC:DD:EE:FF" (or with dashes) as six bytes, or null when it is not one.</summary>
    public static byte[]? ParseMac(string? text)
    {
        var parts = (text ?? string.Empty).Split(':', '-');
        if (parts.Length != MacBytes) return null;
        var mac = new byte[MacBytes];
        for (var i = 0; i < MacBytes; i++)
            if (parts[i].Length != 2 || !byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mac[i])) return null;
        return mac;
    }

    public static byte[] Build(byte[] mac)
    {
        var packet = new byte[MacBytes * (Repeats + 1)];
        Array.Fill(packet, (byte)0xFF, 0, MacBytes);
        for (var i = 0; i < Repeats; i++) Array.Copy(mac, 0, packet, MacBytes * (i + 1), MacBytes);
        return packet;
    }

    /// <summary>The broadcast address of "192.168.1.0/24" (192.168.1.255), or null when the text is not a network.</summary>
    public static IPAddress? BroadcastOf(string? subnet)
    {
        var parts = (subnet ?? string.Empty).Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) || network.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length > 32) return null;
        var address = network.GetAddressBytes();
        for (var bit = length; bit < 32; bit++) address[bit / 8] |= (byte)(0x80 >> (bit % 8));
        return new IPAddress(address);
    }

    /// <summary>Sends the packet for <paramref name="mac"/> to the broadcast of <paramref name="subnet"/>. Call off the UI thread. False (with why) when the address or network is not valid or the send failed.</summary>
    public static bool Send(string mac, string subnet, out string why)
    {
        why = string.Empty;
        if (ParseMac(mac) is not { } bytes || BroadcastOf(subnet) is not { } broadcast)
        {
            why = "Its network card address or network is not known.";
            return false;
        }
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            var packet = Build(bytes);
            udp.Send(packet, packet.Length, new IPEndPoint(broadcast, Port));
            return true;
        }
        catch (SocketException ex)
        {
            why = $"Windows could not send it ({ex.SocketErrorCode}).";
            return false;
        }
    }
}
