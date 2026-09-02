namespace Telumera.Services.Analytics.Api;

public sealed record OverviewMetrics(
    long Visitors, long Sessions, long Views, long EngagedSessions,
    double EngagedSessionRate, double BounceRate,
    double AvgSessionDurationSeconds, long TotalActiveSeconds, bool IsBelowPrivacyFloor);

/// <summary>"...with definitions" (CSV) — Definitions/VisitorCountCaveat are returned inline per the Accuracy Principle rather than left for a client to look up separately.</summary>
public sealed record OverviewResponse(
    DateOnly From, DateOnly To, OverviewMetrics Current, OverviewMetrics? Previous,
    IReadOnlyDictionary<string, string> Definitions, string VisitorCountCaveat);

public sealed record TimeSeriesPoint(DateTimeOffset Bucket, long Sessions, long Visitors, long Views);

public sealed record TimeSeriesResponse(string Interval, IReadOnlyList<TimeSeriesPoint> Points);

public sealed record PageMetric(
    string Path, long Views, long Visitors, long Entries, long Exits, long EngagedViews, bool IsBelowPrivacyFloor);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

public sealed record AcquisitionMetric(
    string Channel, string? UtmSource, string? UtmMedium, string? UtmCampaign,
    long Sessions, long Visitors, long EngagedSessions, bool IsBelowPrivacyFloor);

public sealed record TechnologyMetric(
    string DeviceCategory, string BrowserCategory, string OsCategory,
    long Sessions, long Visitors, long Views, bool IsBelowPrivacyFloor);

/// <summary>Country will be null for every row until M01.8 ships real GeoIP — see services/analytics/README.md.</summary>
public sealed record GeographyMetric(string? Country, long Sessions, long Visitors, long Views, bool IsBelowPrivacyFloor);

public sealed record CustomEventMetric(string EventName, long Count, long UniqueSessions, IReadOnlyList<string> AllowedProperties);
