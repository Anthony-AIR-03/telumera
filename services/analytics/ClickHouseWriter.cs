using System.Text;
using System.Text.Json;

namespace Telumera.Services.Analytics.Api;

/// <summary>One row of `telumera_analytics.events` — property names map to snake_case columns via <see cref="ClickHouseWriter.RowJsonOptions"/>.</summary>
public sealed record AnalyticsEventRow(
    string EventId, string SiteId, string WorkspaceId, string SessionId, string? VisitorId,
    string EventName, string Url, string Path, string QueryString, string? Title, string? Referrer,
    string Channel, string? UtmSource, string? UtmMedium, string? UtmCampaign, string? UtmTerm,
    string? UtmContent, string DeviceCategory, string BrowserCategory, string OsCategory,
    string? Country, int IsBot, string? Environment, string PropertiesJson,
    string ClientTimestamp, string ReceivedAt, string ProcessedAt);

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

    /// <summary>The closest available equivalent to the other services' EF-Core auto-migrate-on-startup — ClickHouse has no EF Core migrations here.</summary>
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => ExecuteAsync(
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

    public Task InsertEventAsync(AnalyticsEventRow row, CancellationToken cancellationToken = default) =>
        ExecuteAsync("INSERT INTO events FORMAT JSONEachRow", cancellationToken, JsonSerializer.Serialize(row, RowJsonOptions));

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
