namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Definitions doc §7 — "Geography granularity: country only, by default." Moved here from
/// services/event-collector (M01.3) per the backlog's own Collector/Analytics split — no GeoIP
/// provider has been chosen anywhere in this repo yet; picking one is out of scope, deferred to M01.8.
/// </summary>
public interface IGeoLookup
{
    string? CountryFor(string truncatedIp);
}

public sealed class NoOpGeoLookup : IGeoLookup
{
    public string? CountryFor(string truncatedIp) => null;
}
