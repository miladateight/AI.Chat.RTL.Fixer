using System.Net;
using System.Net.Sockets;
using AI.ChatRTLFixer.Core.Abstractions;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Picks a random free TCP port on 127.0.0.1 within a range by opening a
/// listener and closing it. Identical to the Windows implementation — this is
/// pure BSD socket behavior with no OS-specific API.
/// </summary>
public sealed class PortPicker : IPortPicker
{
    public int? PickFreePort(int min, int max)
    {
        if (min < 1 || max > 65535 || min > max) return null;
        var rnd = new Random();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = rnd.Next(min, max + 1);
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return port;
            }
            catch (SocketException) { /* in use, try another */ }
        }
        return null;
    }
}
