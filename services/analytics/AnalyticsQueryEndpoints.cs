using System.Text.Json.Nodes;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// M01.6's seven query endpoints, all under GET /sites/{siteId:guid}/analytics/..., all requiring the
/// same 404-then-403 site/workspace check (QueryAuthorization.cs) and Redis response caching
/// (QueryCache.cs). Split into its own file/extension method rather than added to Program.cs directly —
/// no direct precedent for this split elsewhere in the repo, but seven substantial endpoints plus shared
/// auth/cache/date-range plumbing justifies it over one huge Program.cs.
///
/// Every query-string-derived value that isn't validated against a fixed C# allowlist (sort column,
/// sort direction, time-series interval) is passed through ClickHouse's native parameter binding
/// (ClickHouseQueryClient.QueryAsync's `parameters` argument) rather than string-interpolated — verified
/// live against a real ClickHouse instance that this rejects injection attempts as inert literal data.
/// </summary>
public static class AnalyticsQueryEndpoints
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public static void MapAnalyticsQueryEndpoints(this WebApplication app)
    {
        var cacheTtl = TimeSpan.FromSeconds(app.Configuration.GetValue("Cache:TtlSeconds", (int)CacheTtl.TotalSeconds));

        app.MapGet("/sites/{siteId:guid}/analytics/overview", GetOverviewAsync)
            .WithName("GetAnalyticsOverview")
            .RequireAuthorization("ApiScope");

        app.MapGet("/sites/{siteId:guid}/analytics/timeseries", GetTimeSeriesAsync)
            .WithName("GetAnalyticsTimeSeries")
            .RequireAuthorization("ApiScope");

        app.MapGet("/sites/{siteId:guid}/analytics/pages", GetPagesAsync)
            .WithName("GetAnalyticsPages")
            .RequireAuthorization("ApiScope");

        app.MapGet("/sites/{siteId:guid}/analytics/acquisition", GetAcquisitionAsync)
            .WithName("GetAnalyticsAcquisition")
            .RequireAuthorization("ApiScope");

        app.MapGet("/sites/{siteId:guid}/analytics/technology", GetTechnologyAsync)
            .WithName("GetAnalyticsTechnology")
            .RequireAuthorization("ApiScope");

        app.MapGet("/sites/{siteId:guid}/analytics/geography", GetGeographyAsync)
            .WithName("GetAnalyticsGeography")
            .RequireAuthorization("ApiScope");

        app.MapGet("/sites/{siteId:guid}/analytics/events", GetCustomEventsAsync)
            .WithName("GetAnalyticsCustomEvents")
            .RequireAuthorization("ApiScope");

        app.MapGet("/sites/{siteId:guid}/analytics/quality", GetQualityAsync)
            .WithName("GetAnalyticsQuality")
            .RequireAuthorization("ApiScope");

        return;

        async Task<IResult> GetOverviewAsync(
            Guid siteId, HttpContext httpContext, SiteLookupClient siteLookupClient, MembershipClient membershipClient,
            ClickHouseQueryClient clickHouse, QueryCache cache, CancellationToken cancellationToken)
        {
            var auth = await QueryAuthorization.AuthorizeSiteReadAsync(httpContext, siteId, siteLookupClient, membershipClient, cancellationToken);
            if (auth.Error is not null)
            {
                return auth.Error;
            }

            var range = DateRangeParsing.Parse(httpContext);
            if (range is null)
            {
                return Results.BadRequest("Invalid or excessive from/to date range.");
            }

            var compare = httpContext.Request.Query["compare"] == "true";
            var cacheKey = $"analytics:overview:{siteId}:{range.Value.From}:{range.Value.To}:{compare}";

            var response = await cache.GetOrSetAsync(cacheKey, cacheTtl, async () =>
            {
                var current = await QueryOverviewMetricsAsync(clickHouse, siteId, range.Value, cancellationToken);
                OverviewMetrics? previous = compare
                    ? await QueryOverviewMetricsAsync(clickHouse, siteId, DateRangeParsing.GetPreviousPeriod(range.Value), cancellationToken)
                    : null;

                return new OverviewResponse(
                    range.Value.From, range.Value.To, current, previous,
                    OverviewDefinitions,
                    "Visitor counts are the sum of each day's unique visitors under the default cookie-free daily "
                    + "identifier (docs/analytics/definitions-and-privacy-model.md §3) and may overcount a person "
                    + "active across multiple days — cross-day visitor identity is not tracked by design.");
            }, cancellationToken);

            return Results.Ok(response);
        }

        async Task<IResult> GetTimeSeriesAsync(
            Guid siteId, HttpContext httpContext, SiteLookupClient siteLookupClient, MembershipClient membershipClient,
            ClickHouseQueryClient clickHouse, QueryCache cache, CancellationToken cancellationToken)
        {
            var auth = await QueryAuthorization.AuthorizeSiteReadAsync(httpContext, siteId, siteLookupClient, membershipClient, cancellationToken);
            if (auth.Error is not null)
            {
                return auth.Error;
            }

            var range = DateRangeParsing.Parse(httpContext);
            if (range is null)
            {
                return Results.BadRequest("Invalid or excessive from/to date range.");
            }

            var interval = httpContext.Request.Query["interval"].ToString().ToLowerInvariant();
            if (interval is not ("hour" or "day" or "week" or "month"))
            {
                interval = "day";
            }

            var cacheKey = $"analytics:timeseries:{siteId}:{range.Value.From}:{range.Value.To}:{interval}";
            var response = await cache.GetOrSetAsync(cacheKey, cacheTtl, async () =>
            {
                var parameters = new Dictionary<string, string>
                {
                    ["siteId"] = siteId.ToString(),
                    ["from"] = range.Value.From.ToString("yyyy-MM-dd"),
                    ["to"] = range.Value.To.ToString("yyyy-MM-dd"),
                };

                // Rollups are day-grain by design (M01.5) — an hourly request bypasses them and reads
                // `sessions` directly instead of inventing an hourly rollup this epic doesn't ask for.
                var query = interval == "hour"
                    ? """
                      SELECT toStartOfHour(started_at) AS bucket, count() AS sessions,
                          uniqExact(visitor_id) AS visitors, sum(page_view_count) AS views
                      FROM sessions FINAL
                      WHERE site_id = {siteId:String} AND toDate(started_at) BETWEEN {from:Date} AND {to:Date}
                      GROUP BY bucket ORDER BY bucket
                      """
                    : $$"""
                       SELECT {{BucketExpression(interval)}} AS bucket, sum(sessions_count) AS sessions,
                           sum(visitors_count) AS visitors, sum(page_views_count) AS views
                       FROM daily_site_rollup FINAL
                       WHERE site_id = {siteId:String} AND date BETWEEN {from:Date} AND {to:Date}
                       GROUP BY bucket ORDER BY bucket
                       """;

                var rows = await clickHouse.QueryAsync(query, parameters, cancellationToken);
                var points = rows.Select(row => new TimeSeriesPoint(
                    DateTimeOffset.Parse(row.GetText("bucket") + "Z"),
                    row.GetLong("sessions"), row.GetLong("visitors"), row.GetLong("views"))).ToList();

                return new TimeSeriesResponse(interval, points);
            }, cancellationToken);

            return Results.Ok(response);
        }

        async Task<IResult> GetPagesAsync(
            Guid siteId, HttpContext httpContext, SiteLookupClient siteLookupClient, MembershipClient membershipClient,
            ClickHouseQueryClient clickHouse, QueryCache cache, CancellationToken cancellationToken)
        {
            var auth = await QueryAuthorization.AuthorizeSiteReadAsync(httpContext, siteId, siteLookupClient, membershipClient, cancellationToken);
            if (auth.Error is not null)
            {
                return auth.Error;
            }

            var range = DateRangeParsing.Parse(httpContext);
            if (range is null)
            {
                return Results.BadRequest("Invalid or excessive from/to date range.");
            }

            var query = httpContext.Request.Query;
            var search = query["search"].ToString();
            var pathPrefix = query["pathPrefix"].ToString();
            var page = int.TryParse(query["page"], out var p) && p > 0 ? p : 1;
            var pageSize = int.TryParse(query["pageSize"], out var ps) && ps is > 0 and <= 200 ? ps : 25;
            var sortColumn = query["sort"].ToString().ToLowerInvariant() switch
            {
                "visitors" => "visitors",
                "entries" => "entries",
                "exits" => "exits",
                _ => "views",
            };
            var sortDir = string.Equals(query["sortDir"], "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

            var cacheKey = $"analytics:pages:{siteId}:{range.Value.From}:{range.Value.To}:{search}:{pathPrefix}:{page}:{pageSize}:{sortColumn}:{sortDir}";
            var response = await cache.GetOrSetAsync(cacheKey, cacheTtl, async () =>
            {
                var parameters = new Dictionary<string, string>
                {
                    ["siteId"] = siteId.ToString(),
                    ["from"] = range.Value.From.ToString("yyyy-MM-dd"),
                    ["to"] = range.Value.To.ToString("yyyy-MM-dd"),
                    ["search"] = $"%{search}%",
                    ["pathPrefix"] = $"{pathPrefix}%",
                    ["limit"] = pageSize.ToString(),
                    ["offset"] = ((page - 1) * pageSize).ToString(),
                };

                var sql = $$"""
                    SELECT path, sum(views_count) AS views, sum(visitors_count) AS visitors,
                        sum(entries_count) AS entries, sum(exits_count) AS exits, sum(engaged_views_count) AS engaged_views,
                        if(sum(visitors_count) < 5, 1, 0) AS is_below_privacy_floor,
                        count() OVER() AS total_rows
                    FROM daily_page_rollup FINAL
                    WHERE site_id = {siteId:String} AND date BETWEEN {from:Date} AND {to:Date}
                        AND path ILIKE {search:String} AND path LIKE {pathPrefix:String}
                    GROUP BY path
                    ORDER BY {{sortColumn}} {{sortDir}}
                    LIMIT {limit:UInt32} OFFSET {offset:UInt32}
                    """;

                var rows = await clickHouse.QueryAsync(sql, parameters, cancellationToken);
                var items = rows.Select(row => new PageMetric(
                    row.GetText("path"), row.GetLong("views"), row.GetLong("visitors"), row.GetLong("entries"),
                    row.GetLong("exits"), row.GetLong("engaged_views"), row.GetBool("is_below_privacy_floor"))).ToList();
                var total = rows.Count > 0 ? rows[0].GetLong("total_rows") : 0;

                return new PagedResult<PageMetric>(items, total, page, pageSize);
            }, cancellationToken);

            return Results.Ok(response);
        }

        async Task<IResult> GetAcquisitionAsync(
            Guid siteId, HttpContext httpContext, SiteLookupClient siteLookupClient, MembershipClient membershipClient,
            ClickHouseQueryClient clickHouse, QueryCache cache, CancellationToken cancellationToken)
        {
            var auth = await QueryAuthorization.AuthorizeSiteReadAsync(httpContext, siteId, siteLookupClient, membershipClient, cancellationToken);
            if (auth.Error is not null)
            {
                return auth.Error;
            }

            var range = DateRangeParsing.Parse(httpContext);
            if (range is null)
            {
                return Results.BadRequest("Invalid or excessive from/to date range.");
            }

            var cacheKey = $"analytics:acquisition:{siteId}:{range.Value.From}:{range.Value.To}";
            var response = await cache.GetOrSetAsync(cacheKey, cacheTtl, async () =>
            {
                var parameters = SiteRangeParameters(siteId, range.Value);
                var rows = await clickHouse.QueryAsync(
                    """
                    SELECT channel, utm_source, utm_medium, utm_campaign,
                        sum(sessions_count) AS sessions, sum(visitors_count) AS visitors,
                        sum(engaged_sessions_count) AS engaged_sessions, if(sum(visitors_count) < 5, 1, 0) AS is_below_privacy_floor
                    FROM daily_acquisition_rollup FINAL
                    WHERE site_id = {siteId:String} AND date BETWEEN {from:Date} AND {to:Date}
                    GROUP BY channel, utm_source, utm_medium, utm_campaign
                    ORDER BY sessions DESC
                    """,
                    parameters, cancellationToken);

                return rows.Select(row => new AcquisitionMetric(
                    row.GetText("channel"), NullIfEmpty(row.GetText("utm_source")), NullIfEmpty(row.GetText("utm_medium")),
                    NullIfEmpty(row.GetText("utm_campaign")), row.GetLong("sessions"), row.GetLong("visitors"),
                    row.GetLong("engaged_sessions"), row.GetBool("is_below_privacy_floor"))).ToList();
            }, cancellationToken);

            return Results.Ok(response);
        }

        async Task<IResult> GetTechnologyAsync(
            Guid siteId, HttpContext httpContext, SiteLookupClient siteLookupClient, MembershipClient membershipClient,
            ClickHouseQueryClient clickHouse, QueryCache cache, CancellationToken cancellationToken)
        {
            var auth = await QueryAuthorization.AuthorizeSiteReadAsync(httpContext, siteId, siteLookupClient, membershipClient, cancellationToken);
            if (auth.Error is not null)
            {
                return auth.Error;
            }

            var range = DateRangeParsing.Parse(httpContext);
            if (range is null)
            {
                return Results.BadRequest("Invalid or excessive from/to date range.");
            }

            var cacheKey = $"analytics:technology:{siteId}:{range.Value.From}:{range.Value.To}";
            var response = await cache.GetOrSetAsync(cacheKey, cacheTtl, async () =>
            {
                var parameters = SiteRangeParameters(siteId, range.Value);
                var rows = await clickHouse.QueryAsync(
                    """
                    SELECT device_category, browser_category, os_category,
                        sum(sessions_count) AS sessions, sum(visitors_count) AS visitors, sum(page_views_count) AS views,
                        if(sum(visitors_count) < 5, 1, 0) AS is_below_privacy_floor
                    FROM daily_technology_rollup FINAL
                    WHERE site_id = {siteId:String} AND date BETWEEN {from:Date} AND {to:Date}
                    GROUP BY device_category, browser_category, os_category
                    ORDER BY sessions DESC
                    """,
                    parameters, cancellationToken);

                return rows.Select(row => new TechnologyMetric(
                    row.GetText("device_category"), row.GetText("browser_category"), row.GetText("os_category"),
                    row.GetLong("sessions"), row.GetLong("visitors"), row.GetLong("views"),
                    row.GetBool("is_below_privacy_floor"))).ToList();
            }, cancellationToken);

            return Results.Ok(response);
        }

        async Task<IResult> GetGeographyAsync(
            Guid siteId, HttpContext httpContext, SiteLookupClient siteLookupClient, MembershipClient membershipClient,
            ClickHouseQueryClient clickHouse, QueryCache cache, CancellationToken cancellationToken)
        {
            var auth = await QueryAuthorization.AuthorizeSiteReadAsync(httpContext, siteId, siteLookupClient, membershipClient, cancellationToken);
            if (auth.Error is not null)
            {
                return auth.Error;
            }

            var range = DateRangeParsing.Parse(httpContext);
            if (range is null)
            {
                return Results.BadRequest("Invalid or excessive from/to date range.");
            }

            var cacheKey = $"analytics:geography:{siteId}:{range.Value.From}:{range.Value.To}";
            var response = await cache.GetOrSetAsync(cacheKey, cacheTtl, async () =>
            {
                var parameters = SiteRangeParameters(siteId, range.Value);
                var rows = await clickHouse.QueryAsync(
                    """
                    SELECT country, sum(sessions_count) AS sessions, sum(visitors_count) AS visitors, sum(page_views_count) AS views,
                        if(sum(visitors_count) < 5, 1, 0) AS is_below_privacy_floor
                    FROM daily_geography_rollup FINAL
                    WHERE site_id = {siteId:String} AND date BETWEEN {from:Date} AND {to:Date}
                    GROUP BY country
                    ORDER BY sessions DESC
                    """,
                    parameters, cancellationToken);

                return rows.Select(row => new GeographyMetric(
                    NullIfEmpty(row.GetText("country")), row.GetLong("sessions"), row.GetLong("visitors"),
                    row.GetLong("views"), row.GetBool("is_below_privacy_floor"))).ToList();
            }, cancellationToken);

            return Results.Ok(response);
        }

        async Task<IResult> GetCustomEventsAsync(
            Guid siteId, HttpContext httpContext, SiteLookupClient siteLookupClient, MembershipClient membershipClient,
            ClickHouseQueryClient clickHouse, QueryCache cache, CancellationToken cancellationToken)
        {
            var auth = await QueryAuthorization.AuthorizeSiteReadAsync(httpContext, siteId, siteLookupClient, membershipClient, cancellationToken);
            if (auth.Error is not null)
            {
                return auth.Error;
            }

            var range = DateRangeParsing.Parse(httpContext);
            if (range is null)
            {
                return Results.BadRequest("Invalid or excessive from/to date range.");
            }

            var eventName = httpContext.Request.Query["eventName"].ToString();
            var cacheKey = $"analytics:events:{siteId}:{range.Value.From}:{range.Value.To}:{eventName}";

            var response = await cache.GetOrSetAsync(cacheKey, cacheTtl, async () =>
            {
                var parameters = SiteRangeParameters(siteId, range.Value);
                parameters["eventName"] = eventName;

                // eventName = '' matches nothing via equality, so an empty filter must be skipped
                // entirely rather than turned into a no-op "OR {eventName:String} = ''" — simplest is
                // two query text variants, both still fully parameterized.
                var eventFilter = string.IsNullOrEmpty(eventName) ? "" : "AND event_name = {eventName:String}";

                var countRows = await clickHouse.QueryAsync(
                    $$"""
                     SELECT event_name, count() AS event_count, uniqExact(session_id) AS unique_sessions
                     FROM events
                     WHERE site_id = {siteId:String} AND event_name NOT IN ('page_view', 'engagement')
                         AND toDate(client_timestamp) BETWEEN {from:Date} AND {to:Date} {{eventFilter}}
                     GROUP BY event_name
                     ORDER BY event_count DESC
                     """,
                    parameters, cancellationToken);

                var propertyRows = await clickHouse.QueryAsync(
                    $$"""
                     SELECT event_name, groupUniqArray(k) AS keys
                     FROM (
                         SELECT event_name, arrayJoin(JSONExtractKeys(properties_json)) AS k
                         FROM events
                         WHERE site_id = {siteId:String} AND event_name NOT IN ('page_view', 'engagement')
                             AND toDate(client_timestamp) BETWEEN {from:Date} AND {to:Date} {{eventFilter}}
                     )
                     GROUP BY event_name
                     """,
                    parameters, cancellationToken);

                var propertiesByEvent = propertyRows.ToDictionary(
                    row => row.GetText("event_name"),
                    row => (row["keys"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList() ?? []);

                return countRows.Select(row =>
                {
                    var name = row.GetText("event_name");
                    return new CustomEventMetric(
                        name, row.GetLong("event_count"), row.GetLong("unique_sessions"),
                        propertiesByEvent.GetValueOrDefault(name, []));
                }).ToList();
            }, cancellationToken);

            return Results.Ok(response);
        }

        async Task<IResult> GetQualityAsync(
            Guid siteId, HttpContext httpContext, SiteLookupClient siteLookupClient, MembershipClient membershipClient,
            ClickHouseQueryClient clickHouse, QueryCache cache, DeadLetterGauge deadLetterGauge,
            IConfiguration configuration, CancellationToken cancellationToken)
        {
            var auth = await QueryAuthorization.AuthorizeSiteReadAsync(httpContext, siteId, siteLookupClient, membershipClient, cancellationToken);
            if (auth.Error is not null)
            {
                return auth.Error;
            }

            var range = DateRangeParsing.Parse(httpContext);
            if (range is null)
            {
                return Results.BadRequest("Invalid or excessive from/to date range.");
            }

            var delayedThresholdSeconds = configuration.GetValue("Quality:DelayedThresholdSeconds", 120);
            var cacheKey = $"analytics:quality:{siteId}:{range.Value.From}:{range.Value.To}";

            var series = await cache.GetOrSetAsync(cacheKey, cacheTtl, async () =>
            {
                var parameters = SiteRangeParameters(siteId, range.Value);
                parameters["delayed"] = delayedThresholdSeconds.ToString();

                // Collector-published outcome counts (SummingMergeTree — sum(), never FINAL).
                var outcomeRows = await clickHouse.QueryAsync(
                    """
                    SELECT toString(date) AS date, dimension, sum(count) AS c
                    FROM event_quality_daily
                    WHERE site_id = {siteId:String} AND date BETWEEN {from:Date} AND {to:Date}
                    GROUP BY date, dimension
                    """,
                    parameters, cancellationToken);

                // bot / delayed are derived from the durable events table rather than stored — a
                // denormalized copy could drift from the source of truth.
                var derivedRows = await clickHouse.QueryAsync(
                    """
                    SELECT toString(toDate(received_at)) AS date,
                        countIf(is_bot = 1) AS bot,
                        countIf(dateDiff('second', received_at, processed_at) > {delayed:UInt32}) AS delayed
                    FROM events
                    WHERE site_id = {siteId:String} AND toDate(received_at) BETWEEN {from:Date} AND {to:Date}
                    GROUP BY date
                    """,
                    parameters, cancellationToken);

                var byDate = new SortedDictionary<DateOnly, Dictionary<string, long>>();
                Dictionary<string, long> DayBucket(string date) =>
                    byDate.TryGetValue(DateOnly.Parse(date), out var bucket)
                        ? bucket
                        : byDate[DateOnly.Parse(date)] = new Dictionary<string, long>();

                foreach (var row in outcomeRows)
                {
                    DayBucket(row.GetText("date"))[row.GetText("dimension")] = row.GetLong("c");
                }
                foreach (var row in derivedRows)
                {
                    var bucket = DayBucket(row.GetText("date"));
                    bucket["bot"] = row.GetLong("bot");
                    bucket["delayed"] = row.GetLong("delayed");
                }

                return byDate
                    .Select(kv => new QualityDayPoint(kv.Key, kv.Value))
                    .ToList();
            }, cancellationToken);

            var totals = new Dictionary<string, long>();
            foreach (var point in series)
            {
                foreach (var (dimension, count) in point.Dimensions)
                {
                    totals[dimension] = totals.GetValueOrDefault(dimension) + count;
                }
            }

            var response = new QualityResponse(
                range.Value.From, range.Value.To, series, totals,
                Math.Max(0, deadLetterGauge.Depth), QualityDefinitions);

            return Results.Ok(response);
        }
    }

    private static async Task<OverviewMetrics> QueryOverviewMetricsAsync(
        ClickHouseQueryClient clickHouse, Guid siteId, DateRange range, CancellationToken cancellationToken)
    {
        var parameters = SiteRangeParameters(siteId, range);
        var rows = await clickHouse.QueryAsync(
            """
            SELECT
                sum(sessions_count) AS sessions,
                sum(engaged_sessions_count) AS engaged_sessions,
                sum(visitors_count) AS visitors,
                sum(page_views_count) AS views,
                if(sum(sessions_count) = 0, 0, sum(avg_session_duration_seconds * sessions_count) / sum(sessions_count)) AS avg_duration,
                sum(total_active_seconds) AS total_active,
                if(sum(visitors_count) < 5, 1, 0) AS is_below_privacy_floor
            FROM daily_site_rollup FINAL
            WHERE site_id = {siteId:String} AND date BETWEEN {from:Date} AND {to:Date}
            """,
            parameters, cancellationToken);

        if (rows.Count == 0)
        {
            return new OverviewMetrics(0, 0, 0, 0, 0, 0, 0, 0, false);
        }

        var row = rows[0];
        var sessions = row.GetLong("sessions");
        var engaged = row.GetLong("engaged_sessions");
        // Definitions doc §4: bounce rate is only ever published alongside the engaged-session rate it's
        // derived from — the two fields below are always returned together, never bounce rate alone.
        var engagedRate = sessions == 0 ? 0 : (double)engaged / sessions;

        return new OverviewMetrics(
            row.GetLong("visitors"), sessions, row.GetLong("views"), engaged,
            engagedRate, 1 - engagedRate, row.GetDouble("avg_duration"), row.GetLong("total_active"),
            row.GetBool("is_below_privacy_floor"));
    }

    private static Dictionary<string, string> SiteRangeParameters(Guid siteId, DateRange range) => new()
    {
        ["siteId"] = siteId.ToString(),
        ["from"] = range.From.ToString("yyyy-MM-dd"),
        ["to"] = range.To.ToString("yyyy-MM-dd"),
    };

    private static string BucketExpression(string interval) => interval switch
    {
        "week" => "toStartOfWeek(date)",
        "month" => "toStartOfMonth(date)",
        _ => "date",
    };

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static readonly IReadOnlyDictionary<string, string> QualityDefinitions = new Dictionary<string, string>
    {
        ["accepted"] = "Events the collector queued for processing (a retried duplicate the client already sent is not re-counted here).",
        ["rejected_validation"] = "Events that failed schema/size validation at the collector (EventValidation.cs).",
        ["rejected_unknown_token"] = "Batches rejected because the site token didn't resolve — not attributable to any one site, shown at the platform level only.",
        ["rejected_origin"] = "Batches rejected because the Origin/Referer header didn't match the site's allowed origins (defense in depth, ADR 0006).",
        ["rejected_module_disabled"] = "Events dropped because the Analytics module is turned off for the site.",
        ["duplicate"] = "Events whose id was already seen within the collector's 10-minute dedup window — accepted for the client, not re-published.",
        ["dropped_overload"] = "Events dropped because the collector's in-memory publish buffer was full (sustained overload).",
        ["bot"] = "Events written to storage but flagged is_bot (BotDetector.cs) — kept, never silently deleted (definitions doc §8).",
        ["delayed"] = "Events whose accept-to-process gap exceeded the delayed threshold (default 120s) — usually Dapr redelivery/retry lag.",
        ["deadLetterQueueDepth"] = "Messages currently sitting in the analytics dead-letter queue (a point-in-time count, not a per-day total). Recover with tools/dead-letter-recovery.",
    };

    private static readonly IReadOnlyDictionary<string, string> OverviewDefinitions = new Dictionary<string, string>
    {
        ["visitors"] = "Sum of each day's unique visitors (definitions doc §3) — an approximation across multi-day ranges, not a true distinct count.",
        ["sessions"] = "A run of activity from one visitor, ending after 30 minutes of inactivity (definitions doc §2).",
        ["views"] = "Tracked page views (definitions doc §1).",
        ["engagedSessionRate"] = "Share of sessions meeting the engaged-session formula: >=10s active time OR >=2 page views (definitions doc §4).",
        ["bounceRate"] = "1 - engagedSessionRate — published only alongside it, never standalone (definitions doc §4).",
        ["avgSessionDurationSeconds"] = "Mean of started_at-to-ended_at duration across sessions in range.",
        ["totalActiveSeconds"] = "Sum of active time (definitions doc §5) across sessions in range.",
    };
}
