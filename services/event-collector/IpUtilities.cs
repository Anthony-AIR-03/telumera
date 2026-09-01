using System.Net;
using System.Net.Sockets;

namespace Telumera.Services.EventCollector.Api;

/// <summary>
/// Definitions doc §7 — "IP truncation before GeoIP lookup (mask the last IPv4 octet / last 80 bits of
/// IPv6) is a SHOULD, not a MUST... not load-bearing since the raw address is never persisted
/// regardless." Applied here anyway as defense in depth for the transient in-memory window.
/// </summary>
public static class IpUtilities
{
    public static string Truncate(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            bytes[3] = 0; // mask the last IPv4 octet
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            for (var i = 6; i < 16; i++) bytes[i] = 0; // mask the last 80 bits
        }

        return new IPAddress(bytes).ToString();
    }
}
