using System.Net;
using System.Net.Sockets;

namespace Meridian.Core.Net;

/// <summary>
/// Classifies IP addresses for outbound requests whose destination is operator- or
/// provider-supplied. Such requests must only reach public hosts: letting them reach loopback,
/// private, link-local (including the 169.254.169.254 metadata endpoint), or multicast ranges
/// turns the server into a proxy onto its own internal network.
/// </summary>
public static class PublicNetworkAddress
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="address"/> is loopback, unspecified,
    /// private (RFC 1918), carrier-grade NAT, link-local, unique-local, site-local, multicast,
    /// or reserved — anything a server-initiated request must not be allowed to target.
    /// </summary>
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is 0 or 10 or 127 ||
                   (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   bytes[0] >= 224;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
               (address.GetAddressBytes()[0] & 0xfe) == 0xfc;
    }
}
