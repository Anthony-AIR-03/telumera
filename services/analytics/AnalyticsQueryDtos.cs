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

/// <summary>
/// <c>ReferrerHost</c> is the referring domain (no scheme/path/query) — the fallback "source" for a visit
/// that carried no <c>utm_*</c> params, matching GA4/Plausible/Matomo. Null when the visit was direct or
/// the referrer was same-site/unparseable.
/// </summary>
public sealed record AcquisitionMetric(
    string Channel, string? ReferrerHost, string? UtmSource, string? UtmMedium, string? UtmCampaign,
    long Sessions, long Visitors, long EngagedSessions, bool IsBelowPrivacyFloor);

public sealed record TechnologyMetric(
    string DeviceCategory, string BrowserCategory, string OsCategory,
    long Sessions, long Visitors, long Views, bool IsBelowPrivacyFloor);

/// <summary>Country will be null for every row until M01.8 ships real GeoIP — see services/analytics/README.md.</summary>
public sealed record GeographyMetric(string? Country, long Sessions, long Visitors, long Views, bool IsBelowPrivacyFloor);

public sealed record CustomEventMetric(string EventName, long Count, long UniqueSessions, IReadOnlyList<string> AllowedProperties);

/// <summary>
/// M01.8 data-quality dashboard. <c>Series</c> is per-day; each day's <c>Dimensions</c> map holds the
/// collector-published outcome counts (accepted / rejected_* / duplicate / dropped_overload) plus
/// <c>bot</c> and <c>delayed</c> derived from the durable events table. <c>DeadLetterQueueDepth</c> is
/// a point-in-time gauge (messages currently stuck in the DLQ), not a per-day figure.
/// </summary>
public sealed record QualityDayPoint(DateOnly Date, IReadOnlyDictionary<string, long> Dimensions);

public sealed record QualityResponse(
    DateOnly From, DateOnly To,
    IReadOnlyList<QualityDayPoint> Series,
    IReadOnlyDictionary<string, long> Totals,
    long DeadLetterQueueDepth,
    IReadOnlyDictionary<string, string> Definitions);
