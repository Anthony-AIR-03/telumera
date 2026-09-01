using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Telumera.Tests.Integration;

/// <summary>
/// Exercises the full M01.3 → M01.4 pipeline against the real, already-running local Docker Compose
/// stack (see tests/integration/README.md) — requires `docker compose up -d` first. Registers a site,
/// POSTs a batch through the Collector's public /v1/events, and polls ClickHouse's HTTP interface for
/// the normalized/enriched row `services/analytics` writes — mirrors
/// `WorkspaceAndSiteFlowTests.cs`'s outbox-polling pattern, just against ClickHouse instead of Postgres.
/// </summary>
public sealed class AnalyticsProcessingTests
{
    private static readonly HttpClient IdentityWorkspace = new() { BaseAddress = new("http://localhost:5101") };
    private static readonly HttpClient SiteRegistry = new() { BaseAddress = new("http://localhost:5102") };
    private static readonly HttpClient EventCollector = new() { BaseAddress = new("http://localhost:5103") };
    private static readonly HttpClient ClickHouse = CreateClickHouseClient();

    private static readonly string? AccessToken = Environment.GetEnvironmentVariable("TELUMERA_TEST_ACCESS_TOKEN");

    [SkippableFact]
    public async Task PostPageView_IsNormalizedEnrichedAndPersistedToClickHouse()
    {
        var site = await RegisterSiteAsync("Analytics Processing Test", "analytics-test.example");
        var eventId = Guid.NewGuid().ToString();

        var body = new
        {
            Events = new[]
            {
                new
                {
                    Id = eventId,
                    Name = "page_view",
                    SiteToken = site.InitialToken,
                    Environment = "production",
                    SessionId = Guid.NewGuid().ToString(),
                    VisitorId = (string?)null,
                    Url = "https://analytics-test.example/landing/?utm_source=google&utm_medium=cpc#irrelevant-fragment",
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    Properties = new
                    {
                        path = "/landing/",
                        query = new { utm_source = "google", utm_medium = "cpc" },
                        title = "Landing Page",
                        channel = "search",
                        referrer = "https://www.google.com/",
                        utm = new { utm_source = "google", utm_medium = "cpc" },
                    },
                },
            },
        };

        var response = await PostAsync(EventCollector, "/v1/events", body, userAgent:
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        response.EnsureSuccessStatusCode();

        var row = await WaitForClickHouseRowAsync(eventId, TimeSpan.FromSeconds(20));
        Assert.NotNull(row);
        Assert.Equal("/landing", row!["path"]!.GetValue<string>()); // trailing slash + fragment stripped server-side too
        Assert.Equal("google", row["utm_source"]!.GetValue<string>());
        Assert.Equal("cpc", row["utm_medium"]!.GetValue<string>());
        Assert.Equal("search", row["channel"]!.GetValue<string>());
        Assert.Equal("Landing Page", row["title"]!.GetValue<string>());
        Assert.Equal("Chrome", row["browser_category"]!.GetValue<string>());
        Assert.Equal("Windows", row["os_category"]!.GetValue<string>());
        Assert.Equal("Desktop", row["device_category"]!.GetValue<string>());
        Assert.Equal(0, row["is_bot"]!.GetValue<int>());
    }

    [SkippableFact]
    public async Task PostEventTwice_SameId_OnlyOneClickHouseRow()
    {
        var site = await RegisterSiteAsync("Analytics Dedup Test", "analytics-dedup.example");
        var eventId = Guid.NewGuid().ToString();
        var body = new
        {
            Events = new[]
            {
                new
                {
                    Id = eventId,
                    Name = "page_view",
                    SiteToken = site.InitialToken,
                    Environment = "production",
                    SessionId = Guid.NewGuid().ToString(),
                    VisitorId = (string?)null,
                    Url = "https://analytics-dedup.example/",
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    Properties = new { },
                },
            },
        };

        (await PostAsync(EventCollector, "/v1/events", body)).EnsureSuccessStatusCode();
        (await PostAsync(EventCollector, "/v1/events", body)).EnsureSuccessStatusCode();

        Assert.NotNull(await WaitForClickHouseRowAsync(eventId, TimeSpan.FromSeconds(20)));
        await Task.Delay(TimeSpan.FromSeconds(3)); // let a wrongly-duplicated second row have time to appear
        var count = await CountClickHouseRowsAsync(eventId);
        Assert.Equal(1, count);
    }

    [SkippableFact]
    public async Task PostEvent_WithBotUserAgent_IsMarkedNotDropped()
    {
        var site = await RegisterSiteAsync("Analytics Bot Test", "analytics-bot.example");
        var eventId = Guid.NewGuid().ToString();
        var body = new
        {
            Events = new[]
            {
                new
                {
                    Id = eventId,
                    Name = "page_view",
                    SiteToken = site.InitialToken,
                    Environment = "production",
                    SessionId = Guid.NewGuid().ToString(),
                    VisitorId = (string?)null,
                    Url = "https://analytics-bot.example/",
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    Properties = new { },
                },
            },
        };

        (await PostAsync(EventCollector, "/v1/events", body, userAgent: "Googlebot/2.1 (+http://www.google.com/bot.html)"))
            .EnsureSuccessStatusCode();

        var row = await WaitForClickHouseRowAsync(eventId, TimeSpan.FromSeconds(20));
        Assert.NotNull(row);
        Assert.Equal(1, row!["is_bot"]!.GetValue<int>());
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

    private static Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string path, T body, string? userAgent = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (userAgent is not null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }
        return client.SendAsync(request);
    }

    private static async Task<JsonObject?> WaitForClickHouseRowAsync(string eventId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var row = await QueryRowAsync(eventId);
            if (row is not null)
            {
                return row;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        return null;
    }

    private static async Task<JsonObject?> QueryRowAsync(string eventId)
    {
        var query = $"SELECT * FROM events WHERE event_id = '{eventId}' FORMAT JSONEachRow";
        var response = await ClickHouse.PostAsync(
            $"/?database=telumera_analytics&query={Uri.EscapeDataString(query)}", new StringContent(string.Empty));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        // JSONEachRow is newline-delimited; a matching event_id is unique, so the first line is enough.
        var firstLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine is null ? null : JsonNode.Parse(firstLine)?.AsObject();
    }

    private static async Task<int> CountClickHouseRowsAsync(string eventId)
    {
        var query = $"SELECT count() AS c FROM events WHERE event_id = '{eventId}' FORMAT JSONEachRow";
        var response = await ClickHouse.PostAsync(
            $"/?database=telumera_analytics&query={Uri.EscapeDataString(query)}", new StringContent(string.Empty));
        response.EnsureSuccessStatusCode();

        var body = (await response.Content.ReadAsStringAsync()).Trim();
        return JsonNode.Parse(body)?["c"]?.GetValue<int>() ?? 0;
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
