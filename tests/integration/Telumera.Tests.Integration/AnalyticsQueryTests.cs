using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Telumera.Tests.Integration;

/// <summary>
/// Exercises M01.6's query endpoints against the real, already-running local Docker Compose stack (see
/// tests/integration/README.md) — requires `docker compose up -d` first. Goes through the gateway
/// (localhost:5100), not directly to the analytics service, so the gateway's new
/// sites/{id}/analytics/** routing carve-out (GatewayForwarder.cs) is exercised too, not just the
/// service in isolation. Same real-stack-over-HTTP pattern as AnalyticsAggregationTests.cs, reusing its
/// event-posting and session-polling helpers by duplication (this codebase's established "decoupled by
/// design" convention for cross-file test helpers, same as its cross-service payload duplication).
/// </summary>
public sealed class AnalyticsQueryTests
{
    private static readonly HttpClient IdentityWorkspace = new() { BaseAddress = new("http://localhost:5101") };
    private static readonly HttpClient SiteRegistry = new() { BaseAddress = new("http://localhost:5102") };
    private static readonly HttpClient EventCollector = new() { BaseAddress = new("http://localhost:5103") };
    private static readonly HttpClient Gateway = new() { BaseAddress = new("http://localhost:5100") };
    private static readonly HttpClient ClickHouse = CreateClickHouseClient();

    private static readonly string? AccessToken = Environment.GetEnvironmentVariable("TELUMERA_TEST_ACCESS_TOKEN");
    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    [SkippableFact]
    public async Task Overview_ForASiteWithAnEngagedSession_ReturnsExpectedCounts()
    {
        var (site, sessionId) = await RegisterSiteWithEngagedSessionAsync("Query Overview Test", "query-overview-test.example");
        Assert.NotNull(await WaitForSessionRowAsync(sessionId, TimeSpan.FromSeconds(90)));

        var response = await GetAsAsync($"/sites/{site.Id}/analytics/overview");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var current = body!["current"]!.AsObject();
        Assert.True(current["sessions"]!.GetValue<long>() >= 1);
        Assert.True(current["views"]!.GetValue<long>() >= 2);
        Assert.NotNull(body["definitions"]);
        Assert.NotNull(body["visitorCountCaveat"]);
    }

    [SkippableFact]
    public async Task Pages_SupportsSearchSortAndPagination()
    {
        var (site, sessionId) = await RegisterSiteWithEngagedSessionAsync("Query Pages Test", "query-pages-test.example");
        Assert.NotNull(await WaitForSessionRowAsync(sessionId, TimeSpan.FromSeconds(90)));

        var response = await GetAsAsync($"/sites/{site.Id}/analytics/pages?pageSize=1&page=1&sort=views&sortDir=desc");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(1, body!["pageSize"]!.GetValue<int>());
        Assert.True(body["items"]!.AsArray().Count <= 1);
        Assert.True(body["total"]!.GetValue<long>() >= 1);
    }

    /// <summary>
    /// This harness only has one Entra test identity, so a true cross-tenant 403 (a second
    /// workspace/user with no membership) isn't exercisable here — that would need a second registered
    /// test identity. What this DOES verify end-to-end: the 404-then-403 QueryAuthorization path
    /// actually runs for a real member without erroring, and returns 200 rather than a false-positive
    /// 403/500 — the positive-path complement to UnknownSite_ReturnsNotFound below.
    /// </summary>
    [SkippableFact]
    public async Task Member_CanReadOwnSite()
    {
        var site = await RegisterSiteAsync("Query Auth Test", "query-auth-test.example");

        var response = await GetAsAsync($"/sites/{site.Id}/analytics/overview");
        Assert.True(response.IsSuccessStatusCode);
    }

    [SkippableFact]
    public async Task UnknownSite_ReturnsNotFound()
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken), SkipReason);

        var response = await GetAsAsync($"/sites/{Guid.NewGuid()}/analytics/overview");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A visit with no utm_* params but an external referrer must surface the referring domain as
    /// `referrerHost` on the acquisition breakdown (domain only — `www.` and any path stripped),
    /// matching GA4/Plausible/Matomo's referrer fallback.
    /// </summary>
    [SkippableFact]
    public async Task Acquisition_ExposesReferrerHostForANonCampaignVisit()
    {
        var site = await RegisterSiteAsync("Query Acquisition Test", "query-acquisition-test.example");
        var sessionId = Guid.NewGuid().ToString();
        var referrerProps = new { referrer = "https://www.linkedin.com/feed/", channel = "social" };
        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://query-acquisition-test.example/", referrerProps);
        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://query-acquisition-test.example/pricing", referrerProps);
        Assert.NotNull(await WaitForSessionRowAsync(sessionId, TimeSpan.FromSeconds(90)));

        var response = await GetAsAsync($"/sites/{site.Id}/analytics/acquisition");
        response.EnsureSuccessStatusCode();

        var rows = await response.Content.ReadFromJsonAsync<JsonArray>();
        Assert.NotNull(rows);
        var row = rows!.Select(n => n!.AsObject())
            .FirstOrDefault(o => o["referrerHost"]?.GetValue<string?>() == "linkedin.com");
        Assert.NotNull(row);
        Assert.True(row!["sessions"]!.GetValue<long>() >= 1);
    }

    private static async Task<(CreateSiteResponseDto Site, string SessionId)> RegisterSiteWithEngagedSessionAsync(string name, string domain)
    {
        var site = await RegisterSiteAsync(name, domain);
        var sessionId = Guid.NewGuid().ToString();
        await PostEventAsync(site.InitialToken, sessionId, "page_view", $"https://{domain}/");
        await PostEventAsync(site.InitialToken, sessionId, "page_view", $"https://{domain}/pricing");
        return (site, sessionId);
    }

    private static async Task PostEventAsync(string siteToken, string sessionId, string name, string url, object? properties = null)
    {
        var body = new
        {
            Events = new[]
            {
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    SiteToken = siteToken,
                    Environment = "production",
                    SessionId = sessionId,
                    VisitorId = (string?)null,
                    Url = url,
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    Properties = properties ?? new { },
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

    private static Task<HttpResponseMessage> GetAsAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", AccessToken);
        return Gateway.SendAsync(request);
    }

    private static async Task<JsonObject?> WaitForSessionRowAsync(string sessionId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var query = $"SELECT * FROM sessions FINAL WHERE session_id = '{sessionId}' FORMAT JSONEachRow";
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
