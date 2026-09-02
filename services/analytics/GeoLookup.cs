using System.Net;

using MaxMind.Db;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Definitions doc §7 — "Geography granularity: country only, by default." The collecting request's
/// server-observed IP (already truncated by the Collector — last IPv4 octet / last 80 IPv6 bits
/// zeroed) is the only input, and it is never persisted: this lookup runs in-process against a local
/// MMDB file, so no visitor IP ever leaves the box.
/// </summary>
public interface IGeoLookup
{
    string? CountryFor(string truncatedIp);
}

/// <summary>Used when no MMDB database file is present — geography enrichment is simply disabled.</summary>
public sealed class NoOpGeoLookup : IGeoLookup
{
    public string? CountryFor(string truncatedIp) => null;
}

/// <summary>
/// Reads a MaxMind-format <c>.mmdb</c> country database in-process. Works with both MaxMind GeoLite2
/// Country and DB-IP Lite Country (identical file format, same reader) — the provider is a deploy-time
/// choice, not a code one (see docs/runbooks/analytics-module.md and scripts/refresh-geoip.sh).
/// <para>
/// <see cref="DatabaseReader"/> is documented thread-safe for concurrent reads, so this is registered
/// as a singleton.
/// </para>
/// </summary>
public sealed class MmdbGeoLookup : IGeoLookup, IDisposable
{
    private readonly DatabaseReader _reader;

    public MmdbGeoLookup(string databasePath)
    {
        // FileAccessMode.Memory: load the whole file into memory once at startup rather than mmap it —
        // the country DB is a few MB and this keeps lookups off the disk entirely.
        _reader = new DatabaseReader(databasePath, FileAccessMode.Memory);
    }

    public string? CountryFor(string truncatedIp)
    {
        if (string.IsNullOrWhiteSpace(truncatedIp) || !IPAddress.TryParse(truncatedIp, out var ip))
        {
            return null;
        }

        try
        {
            // ISO 3166-1 alpha-2 (e.g. "NL"), or null for an address the DB has no country for
            // (reserved ranges, anonymized/truncated addresses that fall outside any block).
            return _reader.Country(ip).Country.IsoCode;
        }
        catch (AddressNotFoundException)
        {
            return null;
        }
        catch (GeoIP2Exception)
        {
            // Malformed DB row / unexpected record shape — never fail event processing over geography.
            return null;
        }
    }

    public void Dispose() => _reader.Dispose();
}
