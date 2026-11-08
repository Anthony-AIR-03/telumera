namespace Telumera.Services.SiteRegistry.Api;

/// <summary>
/// Shape checks for a new site's domain and allowed origins, beyond what DataAnnotations can
/// express. The event collector compares a browser's <c>Origin</c> header against
/// <c>AllowedOrigins</c> exactly (scheme://host[:port]), so a malformed entry — e.g.
/// <c>https://https://example.com</c> from a domain typed with its scheme — silently rejects every
/// event from that site. Rejecting it here turns that into a 400 at creation time instead.
/// </summary>
internal static class SiteAddressValidation
{
    /// <summary>A bare host name with an optional :port — no scheme, path, query or whitespace.</summary>
    public static bool IsBareDomain(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var host = value;
        var colon = value.LastIndexOf(':');
        if (colon >= 0)
        {
            if (!ushort.TryParse(value.AsSpan(colon + 1), out var port) || port == 0)
            {
                return false;
            }

            host = value[..colon];
        }

        return Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4;
    }

    /// <summary>An origin as browsers send it: http(s)://host[:port], nothing after the authority.</summary>
    public static bool IsOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !IsBareDomain(uri.Host))
        {
            return false;
        }

        return string.Equals(value.TrimEnd('/'), $"{uri.Scheme}://{uri.Authority}", StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string[]> Validate(string canonicalDomain, string[] allowedOrigins)
    {
        var errors = new Dictionary<string, string[]>();

        if (!IsBareDomain(canonicalDomain))
        {
            errors["CanonicalDomain"] = ["Must be a bare domain such as example.com, without https:// or a path."];
        }

        var badOrigins = allowedOrigins.Where(o => !IsOrigin(o)).ToArray();
        if (badOrigins.Length > 0)
        {
            errors["AllowedOrigins"] =
                [$"Each origin must look like https://example.com (no path). Invalid: {string.Join(", ", badOrigins)}"];
        }

        return errors;
    }
}
