using System.Net.Sockets;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.Application.Config;

/// <summary>
/// Probes localhost for a listening IB Gateway/TWS instance on the default API ports.
/// Shared by the configuration services so detection behavior, ports, and timeouts stay
/// consistent instead of drifting across copy-pasted probes.
/// </summary>
public static class IBGatewayProbe
{
    private static readonly ILogger Log = LoggingSetup.ForContext(typeof(IBGatewayProbe));

    /// <summary>Default IB API ports: TWS live/paper (7496/7497), Gateway live/paper (4001/4002).</summary>
    private static readonly int[] DefaultPorts = [7496, 7497, 4001, 4002];

    private static readonly TimeSpan PerPortTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Returns true when any default IB API port accepts a TCP connection on loopback.
    /// </summary>
    public static bool IsAvailable()
    {
        foreach (var port in DefaultPorts)
        {
            try
            {
                using var client = new TcpClient();
                var asyncResult = client.BeginConnect("127.0.0.1", port, null, null);
                if (asyncResult.AsyncWaitHandle.WaitOne(PerPortTimeout) && client.Connected)
                {
                    client.EndConnect(asyncResult);
                    return true;
                }
            }
            catch (SocketException)
            {
                // Nothing listening on this port — the expected miss; try the next one.
            }
            catch (ObjectDisposedException ex)
            {
                Log.Debug(ex, "IB gateway probe raced client disposal on port {Port}", port);
            }
        }

        return false;
    }
}
