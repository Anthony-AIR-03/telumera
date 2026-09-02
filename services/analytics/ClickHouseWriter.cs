using System.Text;
using System.Text.Json;

namespace Telumera.Services.Analytics.Api;

/// <summary>One row of `telumera_analytics.events` — property names map to snake_case columns via <see cref="ClickHouseWriter.RowJsonOptions"/>.</summary>
public sealed record AnalyticsEventRow(
    string EventId, string SiteId, string WorkspaceId, string SessionId, string? VisitorId,
    string EventName, string Url, string Path, string QueryString, string? Title, string? Referrer,
    string Channel, string? UtmSource, string? UtmMedium, string? UtmCampaign, string? UtmTerm,
    string? UtmContent, string DeviceCategory, string BrowserCategory, string OsCategory,
    string? Country, int IsBot, string? Environment, string PropertiesJson, int DataVersion,
    string ClientTimestamp, string ReceivedAt, string ProcessedAt);

/// <summary>One delta row for <c>event_quality_daily</c> (<see cref="ClickHouseWriter.InsertQualityDeltasAsync"/>).</summary>
public sealed record QualityDeltaRow(string SiteId, string Date, string Dimension, long Count);

/// <summary>
/// Writes to ClickHouse via its plain HTTP interface rather than a NuGet client library —
/// ClickHouse.Client's latest stable has no confirmed net10.0 target, an unnecessary compatibility
/// gamble when this codebase already calls every other infra HTTP API directly (Dapr's sidecar,
/// RabbitMQ's management API) instead of through a client SDK.
/// </summary>
public sealed class ClickHouseWriter
{
    private static readonly JsonSerializerOptions RowJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _database;

    public ClickHouseWriter(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(ClickHouseWriter));

        var host = configuration["ClickHouse:Host"] ?? "localhost";
        var port = configuration["ClickHouse:Port"] ?? "8123";
        _database = configuration["ClickHouse:Database"] ?? "telumera_analytics";
        _baseUrl = $"http://{host}:{port}/";

        _httpClient.DefaultRequestHeaders.Add("X-ClickHouse-User", configuration["ClickHouse:User"] ?? "svc_analytics");
        _httpClient.DefaultRequestHeaders.Add("X-ClickHouse-Key", configuration["ClickHouse:Password"] ?? string.Empty);
    }

    /// <summary>
    /// The closest available equivalent to the other services' EF-Core auto-migrate-on-startup —
    /// ClickHouse has no EF Core migrations here. Every statement is idempotent (IF NOT EXISTS / a
    /// MODIFY that's safe to reapply) since this runs on every startup, not just the first one — `events`
    /// already existed before M01.5 (M01.4), so its new column/TTL can't ride along in its own
    /// CREATE TABLE IF NOT EXISTS.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS events
            (
                event_id String,
                site_id String,
                workspace_id String,
                session_id String,
                visitor_id Nullable(String),
                event_name String,
                url String,
                path String,
                query_string String,
                title Nullable(String),
                referrer Nullable(String),
                channel LowCardinality(String),
                utm_source Nullable(String),
                utm_medium Nullable(String),
                utm_campaign Nullable(String),
                utm_term Nullable(String),
                utm_content Nullable(String),
                device_category LowCardinality(String),
                browser_category LowCardinality(String),
                os_category LowCardinality(String),
                country LowCardinality(Nullable(String)),
                is_bot UInt8,
                environment Nullable(String),
                properties_json String,
                client_timestamp DateTime64(3),
                received_at DateTime64(3),
                processed_at DateTime64(3)
            )
            ENGINE = MergeTree
            PARTITION BY toYYYYMM(received_at)
            ORDER BY (site_id, received_at, event_id)
            """,
            cancellationToken);

        // M01.5: "event version" (closes the raw-event-table subtask) and 90-day raw retention
        // (docs/analytics/definitions-and-privacy-model.md §8 — sessions/rollups are the indefinite-
        // retention aggregates, raw events are not). TTL requires a DateTime/Date expression, not
        // DateTime64 directly — ClickHouse 24.8 rejects TTL on a DateTime64 column as-is.
        await ExecuteAsync("ALTER TABLE events ADD COLUMN IF NOT EXISTS data_version UInt16 DEFAULT 1", cancellationToken);
        await ExecuteAsync("ALTER TABLE events MODIFY TTL toDateTime(received_at) + INTERVAL 90 DAY DELETE", cancellationToken);

        // Sessions and the five daily rollups are periodically recomputed (see
        // AnalyticsAggregationService), not streaming materialized views — a 30-minute inactivity gap
        // can't be resolved by an insert-triggered view. Each is ReplacingMergeTree(updated_at): an
        // aggregation pass re-inserts the full row for anything it recomputes, and the newest
        // updated_at wins once merged (query with FINAL, or via the aggregation queries' own re-read of
        // this same table, to see the latest version before a background merge has run).
        await ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS sessions
            (
                session_id String,
                site_id String,
                workspace_id String,
                visitor_id Nullable(String),
                started_at DateTime64(3),
                ended_at DateTime64(3),
                duration_seconds UInt32,
                landing_path String,
                exit_path String,
                page_view_count UInt32,
                event_count UInt32,
                active_seconds UInt32,
                is_engaged UInt8,
                channel LowCardinality(String),
                utm_source Nullable(String),
                utm_medium Nullable(String),
                utm_campaign Nullable(String),
                referrer Nullable(String),
                device_category LowCardinality(String),
                browser_category LowCardinality(String),
                os_category LowCardinality(String),
                country LowCardinality(Nullable(String)),
                is_bot UInt8,
                data_version UInt16,
                updated_at DateTime64(3)
            )
            ENGINE = ReplacingMergeTree(updated_at)
            PARTITION BY toYYYYMM(started_at)
            ORDER BY (site_id, session_id)
            """,
            cancellationToken);

        await ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS daily_site_rollup
            (
                site_id String,
                date Date,
                sessions_count UInt64,
                engaged_sessions_count UInt64,
                visitors_count UInt64,
                page_views_count UInt64,
                avg_session_duration_seconds UInt32,
                total_active_seconds UInt64,
                is_below_privacy_floor UInt8,
                updated_at DateTime64(3)
            )
            ENGINE = ReplacingMergeTree(updated_at)
            PARTITION BY toYYYYMM(date)
            ORDER BY (site_id, date)
            """,
            cancellationToken);

        await ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS daily_page_rollup
            (
                site_id String,
                date Date,
                path String,
                views_count UInt64,
                visitors_count UInt64,
                engaged_views_count UInt64,
                entries_count UInt64,
                exits_count UInt64,
                is_below_privacy_floor UInt8,
                updated_at DateTime64(3)
            )
            ENGINE = ReplacingMergeTree(updated_at)
            PARTITION BY toYYYYMM(date)
            ORDER BY (site_id, date, path)
            """,
            cancellationToken);

        // utm_source/medium/campaign are plain (non-nullable) String here, not Nullable — a ReplacingMergeTree
        // ORDER BY can't contain a nullable column (`allow_nullable_key` is disabled by default), and these
        // three are part of this table's key. Empty string is the "not set" sentinel instead of NULL.
        await ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS daily_acquisition_rollup
            (
                site_id String,
                date Date,
                channel LowCardinality(String),
                utm_source String,
                utm_medium String,
                utm_campaign String,
                sessions_count UInt64,
                visitors_count UInt64,
                engaged_sessions_count UInt64,
                is_below_privacy_floor UInt8,
                updated_at DateTime64(3)
            )
            ENGINE = ReplacingMergeTree(updated_at)
            PARTITION BY toYYYYMM(date)
            ORDER BY (site_id, date, channel, utm_source, utm_medium, utm_campaign)
            """,
            cancellationToken);

        await ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS daily_technology_rollup
            (
                site_id String,
                date Date,
                device_category LowCardinality(String),
                browser_category LowCardinality(String),
                os_category LowCardinality(String),
                sessions_count UInt64,
                visitors_count UInt64,
                page_views_count UInt64,
                is_below_privacy_floor UInt8,
                updated_at DateTime64(3)
            )
            ENGINE = ReplacingMergeTree(updated_at)
            PARTITION BY toYYYYMM(date)
            ORDER BY (site_id, date, device_category, browser_category, os_category)
            """,
            cancellationToken);

        // country is plain String (empty = unknown), not Nullable — same ORDER BY-key restriction as
        // the acquisition rollup above. Will be entirely empty until M01.8 ships real GeoIP (IGeoLookup
        // is still NoOpGeoLookup) — the table is still correct to build now.
        await ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS daily_geography_rollup
            (
                site_id String,
                date Date,
                country LowCardinality(String),
                sessions_count UInt64,
                visitors_count UInt64,
                page_views_count UInt64,
                is_below_privacy_floor UInt8,
                updated_at DateTime64(3)
            )
            ENGINE = ReplacingMergeTree(updated_at)
            PARTITION BY toYYYYMM(date)
            ORDER BY (site_id, date, country)
            """,
            cancellationToken);

        // M01.8 data-quality dashboard: per-(site, day, dimension) counts of ingestion outcomes only
        // this pipeline stage can see — the collector's accept/reject/duplicate/overload tallies,
        // published as collector.quality.v1 delta batches. SummingMergeTree so each delta batch is a
        // plain INSERT that accumulates; reads use `sum(count) ... GROUP BY`, never FINAL. Bot and
        // delayed counts are NOT stored here — they're derived from the durable `events` table at
        // query time (AnalyticsQueryEndpoints' quality handler). site_id '00000000-…' holds
        // unattributable rejections (unknown token). count is Int64 (signed) to tolerate a correction.
        await ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS event_quality_daily
            (
                site_id String,
                date Date,
                dimension LowCardinality(String),
                count Int64
            )
            ENGINE = SummingMergeTree
            PARTITION BY toYYYYMM(date)
            ORDER BY (site_id, date, dimension)
            """,
            cancellationToken);
    }

    public Task InsertEventAsync(AnalyticsEventRow row, CancellationToken cancellationToken = default) =>
        ExecuteAsync("INSERT INTO events FORMAT JSONEachRow", cancellationToken, JsonSerializer.Serialize(row, RowJsonOptions));

    /// <summary>Appends collector.quality.v1 delta rows to the SummingMergeTree — see event_quality_daily's DDL.</summary>
    public Task InsertQualityDeltasAsync(IEnumerable<QualityDeltaRow> rows, CancellationToken cancellationToken = default)
    {
        var body = string.Join('\n', rows.Select(r => JsonSerializer.Serialize(r, RowJsonOptions)));
        return string.IsNullOrEmpty(body)
            ? Task.CompletedTask
            : ExecuteAsync("INSERT INTO event_quality_daily FORMAT JSONEachRow", cancellationToken, body);
    }

    /// <summary>
    /// Recomputes every session that has at least one event landed since <paramref name="watermark"/> —
    /// this is the late-event mechanism (definitions doc §2's 30-minute inactivity rule, M01.5's
    /// "late-event handling" subtask): a late event for a session "closed" days ago makes that
    /// session_id dirty on the very next call, regardless of the session's own age, no separate
    /// window-recalculation logic needed. <paramref name="watermark"/> unset (epoch) naturally
    /// back-fills every pre-existing session the first time this ever runs. See
    /// docs/analytics/definitions-and-privacy-model.md §2, §4, §5 for the exact rules this query
    /// implements (entry-context attribution, engaged-session formula, active-time measurement).
    /// </summary>
    public Task RecomputeSessionsAsync(DateTimeOffset watermark, DateTimeOffset runStartedAt, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            $"""
            INSERT INTO sessions
            SELECT
                session_id,
                any(site_id) AS site_id,
                any(workspace_id) AS workspace_id,
                argMin(visitor_id, client_timestamp) AS visitor_id,
                min(client_timestamp) AS started_at,
                max(client_timestamp) AS ended_at,
                toUInt32(dateDiff('second', min(client_timestamp), max(client_timestamp))) AS duration_seconds,
                argMin(path, client_timestamp) AS landing_path,
                argMax(path, client_timestamp) AS exit_path,
                countIf(event_name = 'page_view') AS page_view_count,
                count() AS event_count,
                toUInt32(sumIf(JSONExtractUInt(properties_json, 'activeMs'), event_name = 'engagement') / 1000) AS active_seconds,
                if(
                    toUInt32(sumIf(JSONExtractUInt(properties_json, 'activeMs'), event_name = 'engagement') / 1000) >= 10
                    OR countIf(event_name = 'page_view') >= 2,
                    1, 0) AS is_engaged,
                argMin(channel, client_timestamp) AS channel,
                argMin(utm_source, client_timestamp) AS utm_source,
                argMin(utm_medium, client_timestamp) AS utm_medium,
                argMin(utm_campaign, client_timestamp) AS utm_campaign,
                argMin(referrer, client_timestamp) AS referrer,
                argMin(device_category, client_timestamp) AS device_category,
                argMin(browser_category, client_timestamp) AS browser_category,
                argMin(os_category, client_timestamp) AS os_category,
                argMin(country, client_timestamp) AS country,
                max(is_bot) AS is_bot,
                max(data_version) AS data_version,
                toDateTime64('{FormatTimestamp(runStartedAt)}', 3) AS updated_at
            FROM events
            WHERE session_id IN (SELECT DISTINCT session_id FROM events WHERE received_at > toDateTime64('{FormatTimestamp(watermark)}', 3))
            GROUP BY session_id
            """,
            cancellationToken);

    /// <summary>
    /// Recomputes the five daily rollups (M01.5's five "Design X rollup" subtasks) for exactly the
    /// (site, date) buckets touched by the sessions <see cref="RecomputeSessionsAsync"/> just wrote this
    /// tick (identified by <paramref name="runStartedAt"/>, the same literal used as those sessions'
    /// updated_at) — this both bounds the recompute to what actually changed and cascades late-event
    /// handling from sessions into rollups with no extra bookkeeping. A rollup row with fewer than 5
    /// distinct visitors is flagged `is_below_privacy_floor`, never suppressed — see
    /// docs/analytics/definitions-and-privacy-model.md §8's addendum for why 5 and why flag-not-hide.
    /// </summary>
    public async Task RecomputeDailyRollupsAsync(DateTimeOffset runStartedAt, CancellationToken cancellationToken = default)
    {
        var runStartedAtLiteral = FormatTimestamp(runStartedAt);
        var dirtyBucketsSql = $"SELECT site_id, toDate(started_at) FROM sessions WHERE updated_at = toDateTime64('{runStartedAtLiteral}', 3)";

        await ExecuteAsync(
            $"""
            INSERT INTO daily_site_rollup
            SELECT
                site_id,
                toDate(started_at) AS date,
                count() AS sessions_count,
                countIf(is_engaged = 1) AS engaged_sessions_count,
                uniqExact(visitor_id) AS visitors_count,
                sum(page_view_count) AS page_views_count,
                toUInt32(avg(duration_seconds)) AS avg_session_duration_seconds,
                sum(active_seconds) AS total_active_seconds,
                if(uniqExact(visitor_id) < 5, 1, 0) AS is_below_privacy_floor,
                toDateTime64('{runStartedAtLiteral}', 3) AS updated_at
            FROM sessions FINAL
            WHERE (site_id, toDate(started_at)) IN ({dirtyBucketsSql})
            GROUP BY site_id, date
            """,
            cancellationToken);

        // views/entries/exits come from different source tables at different grains (per-event views
        // vs. per-session landing/exit) — combined via UNION ALL + max() per metric rather than a
        // FULL OUTER JOIN: each branch only ever populates its own metric column (0 elsewhere), so
        // max() across branches is a correct, simpler combine for the same (site, date, path) key.
        await ExecuteAsync(
            $"""
            INSERT INTO daily_page_rollup
            SELECT
                site_id, date, path, views_count, visitors_count, engaged_views_count, entries_count, exits_count,
                if(visitors_count < 5, 1, 0) AS is_below_privacy_floor,
                toDateTime64('{runStartedAtLiteral}', 3) AS updated_at
            FROM
            (
                SELECT
                    site_id, date, path,
                    max(views_count) AS views_count,
                    max(visitors_count) AS visitors_count,
                    max(engaged_views_count) AS engaged_views_count,
                    max(entries_count) AS entries_count,
                    max(exits_count) AS exits_count
                FROM
                (
                    SELECT site_id, toDate(client_timestamp) AS date, path,
                        count() AS views_count,
                        uniqExact(visitor_id) AS visitors_count,
                        countIf(session_id IN (SELECT session_id FROM sessions WHERE is_engaged = 1)) AS engaged_views_count,
                        toUInt64(0) AS entries_count, toUInt64(0) AS exits_count
                    FROM events
                    WHERE event_name = 'page_view' AND (site_id, toDate(client_timestamp)) IN ({dirtyBucketsSql})
                    GROUP BY site_id, date, path

                    UNION ALL

                    SELECT site_id, toDate(started_at) AS date, landing_path AS path,
                        toUInt64(0) AS views_count, toUInt64(0) AS visitors_count, toUInt64(0) AS engaged_views_count,
                        count() AS entries_count, toUInt64(0) AS exits_count
                    FROM sessions FINAL
                    WHERE (site_id, toDate(started_at)) IN ({dirtyBucketsSql})
                    GROUP BY site_id, date, path

                    UNION ALL

                    SELECT site_id, toDate(started_at) AS date, exit_path AS path,
                        toUInt64(0) AS views_count, toUInt64(0) AS visitors_count, toUInt64(0) AS engaged_views_count,
                        toUInt64(0) AS entries_count, count() AS exits_count
                    FROM sessions FINAL
                    WHERE (site_id, toDate(started_at)) IN ({dirtyBucketsSql})
                    GROUP BY site_id, date, path
                )
                GROUP BY site_id, date, path
            )
            """,
            cancellationToken);

        await ExecuteAsync(
            $"""
            INSERT INTO daily_acquisition_rollup
            SELECT
                site_id, date, channel, utm_source, utm_medium, utm_campaign,
                sessions_count, visitors_count, engaged_sessions_count,
                if(visitors_count < 5, 1, 0) AS is_below_privacy_floor,
                toDateTime64('{runStartedAtLiteral}', 3) AS updated_at
            FROM
            (
                SELECT
                    site_id, toDate(started_at) AS date, channel,
                    coalesce(utm_source, '') AS utm_source,
                    coalesce(utm_medium, '') AS utm_medium,
                    coalesce(utm_campaign, '') AS utm_campaign,
                    count() AS sessions_count,
                    uniqExact(visitor_id) AS visitors_count,
                    countIf(is_engaged = 1) AS engaged_sessions_count
                FROM sessions FINAL
                WHERE (site_id, toDate(started_at)) IN ({dirtyBucketsSql})
                GROUP BY site_id, date, channel, utm_source, utm_medium, utm_campaign
            )
            """,
            cancellationToken);

        await ExecuteAsync(
            $"""
            INSERT INTO daily_technology_rollup
            SELECT
                site_id,
                toDate(started_at) AS date,
                device_category, browser_category, os_category,
                count() AS sessions_count,
                uniqExact(visitor_id) AS visitors_count,
                sum(page_view_count) AS page_views_count,
                if(uniqExact(visitor_id) < 5, 1, 0) AS is_below_privacy_floor,
                toDateTime64('{runStartedAtLiteral}', 3) AS updated_at
            FROM sessions FINAL
            WHERE (site_id, toDate(started_at)) IN ({dirtyBucketsSql})
            GROUP BY site_id, date, device_category, browser_category, os_category
            """,
            cancellationToken);

        await ExecuteAsync(
            $"""
            INSERT INTO daily_geography_rollup
            SELECT
                site_id,
                toDate(started_at) AS date,
                coalesce(country, '') AS country,
                count() AS sessions_count,
                uniqExact(visitor_id) AS visitors_count,
                sum(page_view_count) AS page_views_count,
                if(uniqExact(visitor_id) < 5, 1, 0) AS is_below_privacy_floor,
                toDateTime64('{runStartedAtLiteral}', 3) AS updated_at
            FROM sessions FINAL
            WHERE (site_id, toDate(started_at)) IN ({dirtyBucketsSql})
            GROUP BY site_id, date, country
            """,
            cancellationToken);
    }

    private static string FormatTimestamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");

    private async Task ExecuteAsync(string query, CancellationToken cancellationToken, string? body = null)
    {
        var url = $"{_baseUrl}?database={Uri.EscapeDataString(_database)}&query={Uri.EscapeDataString(query)}";
        using var content = new StringContent(body ?? string.Empty, Encoding.UTF8, "text/plain");
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"ClickHouse request failed ({response.StatusCode}): {error}");
        }
    }
}
