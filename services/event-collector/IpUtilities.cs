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
    /// <summary>
    /// The client IP to feed the visitor hash + GeoIP lookup. Defaults to the raw socket peer
    /// (<see cref="ConnectionInfo.RemoteIpAddress"/>) per the definitions doc's "server-observed,
    /// never client-submitted" rule. In a deployment where the collector is <b>only</b> reachable
    /// through a trusted reverse proxy (NAS: Cloudflare → tunnel → NPM → collector, no host port), the
    /// real client is in a proxy-set header the client itself can't forge — set
    /// <c>Collector:ForwardedForHeader</c> (e.g. <c>CF-Connecting-IP</c>) to trust it. Unset ⇒
    /// unchanged behaviour, so local <c>docker compose</c> is unaffected.
    /// </summary>
    public static IPAddress? ResolveClientAddress(HttpContext httpContext, string? trustedHeaderName)
    {
        if (!string.IsNullOrWhiteSpace(trustedHeaderName)
            && httpContext.Request.Headers.TryGetValue(trustedHeaderName, out var headerValue))
        {
            // X-Forwarded-For style: the left-most entry is the originating client.
            var first = headerValue.ToString().Split(',', 2)[0].Trim();
            if (IPAddress.TryParse(first, out var forwarded))
            {
                return forwarded;
            }
        }

        return httpContext.Connection.RemoteIpAddress;
    }

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
