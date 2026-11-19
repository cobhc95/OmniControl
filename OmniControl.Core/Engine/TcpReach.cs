using System.Net.Sockets;

namespace OmniControl.Core.Engine;

/// Whether something answers at an address and port (a plain connect, nothing is sent).
public static class TcpReach
{
    public static async Task<bool> CanConnectAsync(string host, int port, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(CoreTiming.TcpReachWaitMs);
        try
        {
            await client.ConnectAsync(host, port, limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }
}
