namespace Telumera.Services.EventCollector.Api;

/// <summary>
/// Definitions doc §7 — "Geography granularity: country only, by default." No GeoIP provider has been
/// chosen anywhere in this repo yet; picking one is out of scope for this epic and explicitly deferred
/// to M01.8 per the privacy doc's own "still worth a final check against current guidance" note. This
/// interface exists so a real provider can be dropped in later without touching the collection
/// pipeline.
/// </summary>
public interface IGeoLookup
{
    string? CountryFor(string truncatedIp);
}

public sealed class NoOpGeoLookup : IGeoLookup
{
    public string? CountryFor(string truncatedIp) => null;
}
