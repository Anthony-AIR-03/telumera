using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Telumera.Tests.Integration;

/// <summary>
/// Exercises M01.5's session/rollup aggregation against the real, already-running local Docker Compose
/// stack (see tests/integration/README.md) — requires `docker compose up -d` first. Posts events through
/// the Collector's public /v1/events (same entry point AnalyticsProcessingTests.cs uses for M01.3/M01.4),
/// then polls ClickHouse's HTTP interface for the `sessions`/`daily_site_rollup` rows
/// services/analytics/AnalyticsAggregationService writes on its own periodic timer — no test-only trigger
/// endpoint exists, so polling has to span at least one real aggregation tick
/// (Aggregation:IntervalSeconds, 60s by default).
/// </summary>
public sealed class AnalyticsAggregationTests
{
    private static readonly HttpClient IdentityWorkspace = new() { BaseAddress = new("http://localhost:5101") };
    private static readonly HttpClient SiteRegistry = new() { BaseAddress = new("http://localhost:5102") };
    private static readonly HttpClient EventCollector = new() { BaseAddress = new("http://localhost:5103") };
    private static readonly HttpClient ClickHouse = CreateClickHouseClient();

    private static readonly string? AccessToken = Environment.GetEnvironmentVariable("TELUMERA_TEST_ACCESS_TOKEN");
    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    [SkippableFact]
    public async Task MultiPageEngagedSession_IsAggregatedIntoSessionsRow()
    {
        var site = await RegisterSiteAsync("Aggregation Session Test", "aggregation-session-test.example");
        var sessionId = Guid.NewGuid().ToString();

        // Landing page view, then a second page view — two page views alone satisfies definitions doc
        // §4's engaged-session formula, independent of active-time.
        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://aggregation-session-test.example/");
        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://aggregation-session-test.example/pricing");

        var row = await WaitForSessionRowAsync(sessionId, TimeSpan.FromSeconds(90));
        Assert.NotNull(row);
        Assert.Equal(2, row!["page_view_count"]!.GetValue<int>());
        Assert.Equal(1, row["is_engaged"]!.GetValue<int>());
        Assert.Equal("/", row["landing_path"]!.GetValue<string>());
        Assert.Equal("/pricing", row["exit_path"]!.GetValue<string>());
    }

    [SkippableFact]
    public async Task SingleUnansweredPageView_IsNotEngaged()
    {
        var site = await RegisterSiteAsync("Aggregation Bounce Test", "aggregation-bounce-test.example");
        var sessionId = Guid.NewGuid().ToString();

        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://aggregation-bounce-test.example/");

        var row = await WaitForSessionRowAsync(sessionId, TimeSpan.FromSeconds(90));
        Assert.NotNull(row);
        Assert.Equal(1, row!["page_view_count"]!.GetValue<int>());
        Assert.Equal(0, row["is_engaged"]!.GetValue<int>());
    }

    [SkippableFact]
    public async Task LateEvent_ForAnAlreadyAggregatedSession_UpdatesTheSessionRowOnTheNextTick()
    {
        var site = await RegisterSiteAsync("Aggregation Late Event Test", "aggregation-late-test.example");
        var sessionId = Guid.NewGuid().ToString();

        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://aggregation-late-test.example/");
        var firstRow = await WaitForSessionRowAsync(sessionId, TimeSpan.FromSeconds(90));
        Assert.NotNull(firstRow);
        Assert.Equal(1, firstRow!["page_view_count"]!.GetValue<int>());

        // A second page view for the same session, arriving after the first aggregation pass already
        // wrote a row — proves late-event handling (ClickHouseWriter.RecomputeSessionsAsync's watermark)
        // actually reprocesses this session_id on a later tick, not just on first sight.
        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://aggregation-late-test.example/checkout");

        var updatedRow = await WaitForSessionRowWithPageViewCountAsync(sessionId, expectedCount: 2, TimeSpan.FromSeconds(150));
        Assert.NotNull(updatedRow);
        Assert.Equal("/checkout", updatedRow!["exit_path"]!.GetValue<string>());
        Assert.Equal(1, updatedRow["is_engaged"]!.GetValue<int>());
    }

    [SkippableFact]
    public async Task EngagedSession_IsReflectedInDailySiteRollup()
    {
        var site = await RegisterSiteAsync("Aggregation Rollup Test", "aggregation-rollup-test.example");
        var sessionId = Guid.NewGuid().ToString();

        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://aggregation-rollup-test.example/");
        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://aggregation-rollup-test.example/about");
        Assert.NotNull(await WaitForSessionRowAsync(sessionId, TimeSpan.FromSeconds(90)));

        var rollupRow = await WaitForDailySiteRollupRowAsync(site.Id.ToString(), TimeSpan.FromSeconds(30));
        Assert.NotNull(rollupRow);
        Assert.True(rollupRow!["sessions_count"]!.GetValue<string>() is not "0");
        Assert.True(rollupRow["engaged_sessions_count"]!.GetValue<string>() is not "0");
    }

    private static async Task PostEventAsync(string siteToken, string sessionId, string name, string url)
    {
        var eventId = Guid.NewGuid().ToString();
        var body = new
        {
            Events = new[]
            {
                new
                {
                    Id = eventId,
                    Name = name,
                    SiteToken = siteToken,
                    Environment = "production",
                    SessionId = sessionId,
                    VisitorId = (string?)null,
                    Url = url,
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    Properties = new { },
                },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/events") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("User-Agent", ChromeUserAgent);
        var response = await EventCollector.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<CreateSiteResponseDto> RegisterSiteAsync(string name, string domain)
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken), SkipReason);

        var workspaceResponse = await PostAsAsync(IdentityWorkspace, "/workspaces", new { Name = $"{name} Workspace" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(workspace);

        var siteResponse = await PostAsAsync(SiteRegistry, "/sites", new
        {
            WorkspaceId = workspace!.Id,
            Name = name,
            CanonicalDomain = domain,
            AllowedOrigins = new[] { $"https://{domain}" },
            Environment = "production",
        });
        siteResponse.EnsureSuccessStatusCode();
        var site = await siteResponse.Content.ReadFromJsonAsync<CreateSiteResponseDto>();
        Assert.NotNull(site);
        return site!;
    }

    private static Task<HttpResponseMessage> PostAsAsync<T>(HttpClient client, string path, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", AccessToken);
        return client.SendAsync(request);
    }

    private static async Task<JsonObject?> WaitForSessionRowAsync(string sessionId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var row = await QuerySessionRowAsync(sessionId);
            if (row is not null)
            {
                return row;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        return null;
    }

    private static async Task<JsonObject?> WaitForSessionRowWithPageViewCountAsync(string sessionId, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var row = await QuerySessionRowAsync(sessionId);
            if (row is not null && row["page_view_count"]!.GetValue<int>() == expectedCount)
            {
                return row;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        return null;
    }

    private static async Task<JsonObject?> QuerySessionRowAsync(string sessionId)
    {
        var query = $"SELECT * FROM sessions FINAL WHERE session_id = '{sessionId}' FORMAT JSONEachRow";
        var response = await ClickHouse.PostAsync(
            $"/?database=telumera_analytics&query={Uri.EscapeDataString(query)}", new StringContent(string.Empty));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var firstLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine is null ? null : JsonNode.Parse(firstLine)?.AsObject();
    }

    private static async Task<JsonObject?> WaitForDailySiteRollupRowAsync(string siteId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var query = $"SELECT * FROM daily_site_rollup FINAL WHERE site_id = '{siteId}' FORMAT JSONEachRow";
            var response = await ClickHouse.PostAsync(
                $"/?database=telumera_analytics&query={Uri.EscapeDataString(query)}", new StringContent(string.Empty));
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var firstLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstLine is not null)
            {
                return JsonNode.Parse(firstLine)?.AsObject();
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        return null;
    }

    private static HttpClient CreateClickHouseClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("http://localhost:8123") };
        var password = Environment.GetEnvironmentVariable("CLICKHOUSE_APP_PASSWORD") ?? "change-me-local-dev";
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-ClickHouse-User", "svc_analytics");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-ClickHouse-Key", password);
        return client;
    }

    private const string SkipReason =
        "TELUMERA_TEST_ACCESS_TOKEN not set — run infrastructure/compose/scripts/get-dev-token.sh " +
        "(or .ps1) and export the result to run this test.";

    private sealed record WorkspaceDto(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record CreateSiteResponseDto(
        Guid Id, Guid WorkspaceId, string Name, string CanonicalDomain, string[] AllowedOrigins,
        string Environment, DateTimeOffset CreatedAt, string InitialToken);
}
