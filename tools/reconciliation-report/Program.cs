using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

using Npgsql;

// M01.5's "Implement data reconciliation job" subtask — compares accepted/processed/stored event counts
// for one UTC date, following tools/dead-letter-recovery's own established shape (bare console tool, no
// compose service, run on demand). Three of the numbers are now exact and directly comparable:
//
//   Accepted  — ClickHouse `event_quality_daily`, dimension 'accepted' (M01.8): the collector's own
//   per-day tally of events it queued for processing, published as collector.quality.v1 deltas. This
//   replaces the old RabbitMQ cumulative counter as the exact per-day "accepted" figure.
//   Processed — Postgres `processed_events` (telumera_analytics_control): every event EventProcessor
//   began handling gets a marker row here, committed BEFORE the ClickHouse write
//   (services/analytics/README.md's disclosed crash-window trade-off).
//   Stored     — ClickHouse `events`: the actual row EventProcessor wrote after the marker.
//
// processed > stored is exactly that disclosed trade-off surfacing for real. accepted > stored is
// expected while events are in flight; a persistent gap means events are stuck in the dead-letter queue.
//
// The RabbitMQ exchange publish_in counters (still shown at the bottom) stay context-only — cumulative
// since the broker last reset, not scoped to the date. Showing them with false per-day precision would
// violate the Accuracy Principle (docs/architecture/vision-and-scope.md §5) this whole module is built around.
//
// Usage:
//   dotnet run --project tools/reconciliation-report -- [--date yyyy-MM-dd]
// (defaults to today, UTC, if --date is omitted)

var date = DateOnly.TryParse(GetOption(args, "--date"), out var parsedDate)
    ? parsedDate
    : DateOnly.FromDateTime(DateTime.UtcNow);

var postgresConnectionString = Environment.GetEnvironmentVariable("TELUMERA_ANALYTICS_CONTROL_CONNECTION_STRING")
    ?? throw new InvalidOperationException("TELUMERA_ANALYTICS_CONTROL_CONNECTION_STRING is not set.");
var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_HTTP_URL") ?? "http://localhost:8123";
var clickHouseUser = Environment.GetEnvironmentVariable("CLICKHOUSE_USER") ?? "svc_analytics";
var clickHousePassword = Environment.GetEnvironmentVariable("CLICKHOUSE_APP_PASSWORD")
    ?? throw new InvalidOperationException("CLICKHOUSE_APP_PASSWORD is not set.");
var rabbitManagementUrl = Environment.GetEnvironmentVariable("RABBITMQ_MANAGEMENT_URL") ?? "http://localhost:15672";
var rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_USER");
var rabbitPassword = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_PASS");

var processedCount = await GetProcessedCountAsync(postgresConnectionString, date);
var (storedCount, botCount) = await GetStoredCountsAsync(clickHouseUrl, clickHouseUser, clickHousePassword, date);
var acceptedCount = await GetAcceptedCountAsync(clickHouseUrl, clickHouseUser, clickHousePassword, date);

Console.WriteLine($"Reconciliation report for {date:yyyy-MM-dd} (UTC)");
Console.WriteLine("----------------------------------------------------");
Console.WriteLine($"Accepted  (ClickHouse event_quality_daily, exact): {acceptedCount}");
Console.WriteLine($"Processed (Postgres processed_events, exact)     : {processedCount}");
Console.WriteLine($"Stored    (ClickHouse events, exact)             : {storedCount}");
Console.WriteLine($"  of which flagged is_bot (kept, not dropped)    : {botCount}");

if (acceptedCount > 0 && acceptedCount > storedCount)
{
    Console.WriteLine();
    Console.WriteLine($"NOTE: {acceptedCount - storedCount} event(s) the collector accepted are not yet in ClickHouse for this date.");
    Console.WriteLine("Expected while events are in flight; a persistent gap means events are stuck in the dead-letter queue (tools/dead-letter-recovery).");
}

if (processedCount > storedCount)
{
    Console.WriteLine();
    Console.WriteLine($"WARNING: {processedCount - storedCount} event(s) marked processed but never written to ClickHouse.");
    Console.WriteLine("This is the disclosed marker-before-write crash-window trade-off (services/analytics/README.md) — not a bug in this tool.");
}
else if (processedCount < storedCount)
{
    Console.WriteLine();
    Console.WriteLine($"WARNING: {storedCount - processedCount} more ClickHouse row(s) than processed markers — unexpected, investigate.");
}

if (!string.IsNullOrWhiteSpace(rabbitUser) && !string.IsNullOrWhiteSpace(rabbitPassword))
{
    var (accepted, publishedProcessed) = await GetRabbitCumulativeCountsAsync(rabbitManagementUrl, rabbitUser, rabbitPassword);
    Console.WriteLine();
    Console.WriteLine("Accepted/published (RabbitMQ, cumulative since broker start — NOT scoped to this date, context only):");
    Console.WriteLine($"  collector-events publish_in  : {accepted}");
    Console.WriteLine($"  analytics-events publish_in  : {publishedProcessed}");
}
else
{
    Console.WriteLine();
    Console.WriteLine("(Set RABBITMQ_DEFAULT_USER/RABBITMQ_DEFAULT_PASS to also show cumulative accepted/published counters for context.)");
}

return 0;

static async Task<long> GetProcessedCountAsync(string connectionString, DateOnly date)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    // processed_events also holds dedup markers for collector.quality.v1 (M01.8) — those never produce
    // a ClickHouse `events` row, so exclude them to keep "processed" comparable to "stored".
    await using var command = new NpgsqlCommand(
        """
        SELECT count(*) FROM processed_events
        WHERE (processed_at AT TIME ZONE 'UTC')::date = @date
          AND event_type IN ('analytics.page-view.received.v1', 'analytics.custom-event.received.v1')
        """, connection);
    command.Parameters.AddWithValue("date", date);

    return (long)(await command.ExecuteScalarAsync() ?? 0L);
}

static async Task<(long Stored, long Bot)> GetStoredCountsAsync(string baseUrl, string user, string password, DateOnly date)
{
    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-ClickHouse-User", user);
    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-ClickHouse-Key", password);

    var stored = await RunClickHouseCountAsync(httpClient, baseUrl,
        $"SELECT count() AS c FROM events WHERE toDate(processed_at) = '{date:yyyy-MM-dd}'");
    var bot = await RunClickHouseCountAsync(httpClient, baseUrl,
        $"SELECT count() AS c FROM events WHERE toDate(processed_at) = '{date:yyyy-MM-dd}' AND is_bot = 1");

    return (stored, bot);
}

static async Task<long> GetAcceptedCountAsync(string baseUrl, string user, string password, DateOnly date)
{
    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-ClickHouse-User", user);
    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-ClickHouse-Key", password);

    // event_quality_daily is a SummingMergeTree of collector.quality.v1 deltas — sum, never FINAL.
    return await RunClickHouseCountAsync(httpClient, baseUrl,
        $"SELECT sum(count) AS c FROM event_quality_daily WHERE date = '{date:yyyy-MM-dd}' AND dimension = 'accepted'");
}

static async Task<long> RunClickHouseCountAsync(HttpClient httpClient, string baseUrl, string query)
{
    var url = $"{baseUrl}/?database=telumera_analytics&query={Uri.EscapeDataString(query + " FORMAT JSONEachRow")}";
    using var response = await httpClient.PostAsync(url, new StringContent(string.Empty));
    response.EnsureSuccessStatusCode();

    var body = (await response.Content.ReadAsStringAsync()).Trim();
    var node = JsonNode.Parse(body);
    // ClickHouse's JSON output formats quote UInt64 values as strings (output_format_json_quote_64bit_integers
    // defaults on, avoiding precision loss for JS-style consumers) — parse as string, not GetValue<long>().
    return long.Parse(node?["c"]?.GetValue<string>() ?? "0");
}

static async Task<(string Accepted, string Processed)> GetRabbitCumulativeCountsAsync(string managementUrl, string user, string password)
{
    using var httpClient = new HttpClient { BaseAddress = new Uri(managementUrl) };
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    var accepted = await GetExchangePublishInAsync(httpClient, "collector-events");
    var processed = await GetExchangePublishInAsync(httpClient, "analytics-events");
    return (accepted, processed);
}

static async Task<string> GetExchangePublishInAsync(HttpClient httpClient, string exchange)
{
    using var response = await httpClient.GetAsync($"/api/exchanges/%2f/{Uri.EscapeDataString(exchange)}");
    if (!response.IsSuccessStatusCode)
    {
        return $"(unavailable: {response.StatusCode})";
    }

    var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
    return json?["message_stats"]?["publish_in"]?.GetValue<long>().ToString() ?? "0 (no publishes yet)";
}

static string? GetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
