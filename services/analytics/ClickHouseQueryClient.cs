using System.Text.Json;
using System.Text.Json.Nodes;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Read-only ClickHouse HTTP client for the query endpoints — a separate class/HttpClient from
/// ClickHouseWriter.cs on purpose: reads need a short, bounded timeout (dashboard requests must not
/// hang), the aggregation writer must not be cut off mid-recompute. Every query gets both a client-side
/// HttpClient.Timeout AND a server-side `SETTINGS max_execution_time` — the client timeout alone only
/// stops this service from waiting, it doesn't stop ClickHouse itself from burning CPU on a runaway
/// query, which is the actual "protect ClickHouse" goal (M01.6's cache+timeout subtask).
/// </summary>
public sealed class ClickHouseQueryClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _database;
    private readonly int _maxExecutionTimeSeconds;

    public ClickHouseQueryClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(ClickHouseQueryClient));

        var timeoutSeconds = configuration.GetValue("ClickHouse:QueryTimeoutSeconds", 10);
        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        // A couple of seconds below the HTTP timeout so ClickHouse aborts itself server-side before
        // the client gives up waiting for a response that's never coming.
        _maxExecutionTimeSeconds = Math.Max(1, timeoutSeconds - 2);

        var host = configuration["ClickHouse:Host"] ?? "localhost";
        var port = configuration["ClickHouse:Port"] ?? "8123";
        _database = configuration["ClickHouse:Database"] ?? "telumera_analytics";
        _baseUrl = $"http://{host}:{port}/";

        _httpClient.DefaultRequestHeaders.Add("X-ClickHouse-User", configuration["ClickHouse:User"] ?? "svc_analytics");
        _httpClient.DefaultRequestHeaders.Add("X-ClickHouse-Key", configuration["ClickHouse:Password"] ?? string.Empty);
    }

    /// <summary>
    /// Runs a SELECT (no trailing FORMAT/SETTINGS clause — this method appends both) and returns each
    /// row as a JsonObject. Any query-string-derived value that isn't a C#-validated keyword (a sort
    /// column checked against an allowlist, an interval name, etc.) must go through
    /// <paramref name="parameters"/> — ClickHouse's native `{name:Type}` binding, verified live to
    /// reject injection attempts (a value containing `' OR '1'='1` is treated as inert literal data,
    /// never as SQL) — rather than string-interpolated into <paramref name="selectQuery"/>.
    /// </summary>
    public async Task<List<JsonObject>> QueryAsync(
        string selectQuery, IReadOnlyDictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        var boundedQuery = $"{selectQuery} SETTINGS max_execution_time = {_maxExecutionTimeSeconds} FORMAT JSONEachRow";
        var url = $"{_baseUrl}?database={Uri.EscapeDataString(_database)}&query={Uri.EscapeDataString(boundedQuery)}";
        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                url += $"&param_{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
            }
        }

        using var response = await _httpClient.PostAsync(url, new StringContent(string.Empty), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"ClickHouse query failed ({response.StatusCode}): {error}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToList();
    }
}

/// <summary>
/// ClickHouse's JSON output formats quote UInt64/Int64 values as strings by default
/// (output_format_json_quote_64bit_integers, avoids precision loss for JS-style consumers) but leave
/// UInt32/Float64/etc. unquoted — these helpers branch on the parsed JsonElement's actual ValueKind
/// rather than assuming either representation, so they're correct regardless of column width.
/// </summary>
public static class ClickHouseJsonExtensions
{
    public static long GetLong(this JsonObject row, string key)
    {
        var element = row[key]?.GetValue<JsonElement>() ?? default;
        return element.ValueKind == JsonValueKind.String ? long.Parse(element.GetString()!) : element.GetInt64();
    }

    public static double GetDouble(this JsonObject row, string key)
    {
        var element = row[key]?.GetValue<JsonElement>() ?? default;
        return element.ValueKind == JsonValueKind.String ? double.Parse(element.GetString()!) : element.GetDouble();
    }

    public static string GetText(this JsonObject row, string key) => row[key]?.GetValue<string>() ?? string.Empty;

    public static string? GetNullableText(this JsonObject row, string key)
    {
        var node = row[key];
        return node is null || node.GetValueKind() == JsonValueKind.Null ? null : node.GetValue<string>();
    }

    public static bool GetBool(this JsonObject row, string key) => row.GetLong(key) != 0;
}
