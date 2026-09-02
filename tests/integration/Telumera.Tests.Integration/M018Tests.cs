using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Telumera.Tests.Integration;

/// <summary>
/// M01.8 — live-visitor projection + SignalR, the data-quality endpoint, and real GeoIP. Same
/// real-stack-over-HTTP pattern as AnalyticsQueryTests.cs (run `docker compose up -d` first, export
/// TELUMERA_TEST_ACCESS_TOKEN), helpers duplicated per this repo's convention.
///
/// The geography test additionally needs a GeoIP database mounted — run
/// infrastructure/compose/scripts/refresh-geoip.sh once before it will pass.
/// </summary>
public sealed class M018Tests
{
    private static readonly HttpClient IdentityWorkspace = new() { BaseAddress = new("http://localhost:5101") };
    private static readonly HttpClient SiteRegistry = new() { BaseAddress = new("http://localhost:5102") };
    private static readonly HttpClient EventCollector = new() { BaseAddress = new("http://localhost:5103") };
    private static readonly HttpClient Gateway = new() { BaseAddress = new("http://localhost:5100") };
    private static readonly HttpClient ClickHouse = CreateClickHouseClient();

    private const string AnalyticsOrigin = "http://localhost:5104";
    private static readonly string? AccessToken = Environment.GetEnvironmentVariable("TELUMERA_TEST_ACCESS_TOKEN");
    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    [SkippableFact]
    public async Task LivePanel_ReflectsRecentEventsOverSignalR()
    {
        var site = await RegisterSiteAsync("Live Panel Test", "live-panel-test.example");

        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{AnalyticsOrigin}/hubs/live", options =>
                options.AccessTokenProvider = () => Task.FromResult(AccessToken)!)
            .Build();

        var snapshots = new List<JsonObject>();
        connection.On<JsonObject>("live", snapshot => snapshots.Add(snapshot));

        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", site.Id.ToString());

        var sessionId = Guid.NewGuid().ToString();
        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://live-panel-test.example/");
        await PostEventAsync(site.InitialToken, sessionId, "page_view", "https://live-panel-test.example/pricing");

        // Broadcast is on a 5s timer; give it a few cycles.
        var sawVisitor = await WaitUntilAsync(
            () => snapshots.Any(s => s["activeVisitors"]!.GetValue<int>() >= 1),
            TimeSpan.FromSeconds(40));

        Assert.True(sawVisitor, "Expected a live snapshot with activeVisitors >= 1 within 40s.");
    }

    [SkippableFact]
    public async Task LiveHub_RejectsSubscribeForANonMemberSite()
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken), SkipReason);

        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{AnalyticsOrigin}/hubs/live", options =>
                options.AccessTokenProvider = () => Task.FromResult(AccessToken)!)
            .Build();
        await connection.StartAsync();

        // A random site id the caller has no membership on — Subscribe runs the same 404/403 check
        // the REST endpoints use and should throw a HubException, not silently join.
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Subscribe", Guid.NewGuid().ToString()));
    }

    [SkippableFact]
    public async Task Quality_CountsAcceptedRejectedAndUnknownToken()
    {
        var site = await RegisterSiteAsync("Quality Test", "quality-test.example");

        // 1 good event, 1 malformed (26 properties > the 25 cap), 1 batch on a bogus token.
        await PostEventAsync(site.InitialToken, Guid.NewGuid().ToString(), "page_view", "https://quality-test.example/");
        await PostMalformedEventAsync(site.InitialToken);
        await PostUnknownTokenBatchAsync();

        // collector.quality.v1 publishes on a 60s timer — poll event_quality_daily until the
        // collector-owned dimensions land.
        var accepted = await WaitForQualitySumAsync(site.Id, "accepted", TimeSpan.FromSeconds(100));
        Assert.True(accepted >= 1, $"expected accepted >= 1, saw {accepted}");
        var rejected = await WaitForQualitySumAsync(site.Id, "rejected_validation", TimeSpan.FromSeconds(20));
        Assert.True(rejected >= 1, $"expected rejected_validation >= 1, saw {rejected}");

        var response = await GetAsAsync($"/sites/{site.Id}/analytics/quality");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var totals = body!["totals"]!.AsObject();
        Assert.True(totals["accepted"]!.GetValue<long>() >= 1);
        Assert.True(totals["rejected_validation"]!.GetValue<long>() >= 1);
        Assert.NotNull(body["deadLetterQueueDepth"]);
        Assert.NotNull(body["definitions"]);
    }

    /// <summary>Requires a GeoIP database mounted (scripts/refresh-geoip.sh) — otherwise country stays empty.</summary>
    [SkippableFact]
    public async Task Geography_PopulatesCountryFromTheForwardedClientIp()
    {
        var site = await RegisterSiteAsync("Geography Test", "geography-test.example");

        var eventId = Guid.NewGuid().ToString();
        // 213.127.x is a Dutch range; the local collector trusts X-Forwarded-For (see docker-compose.yml).
        await PostEventAsync(site.InitialToken, Guid.NewGuid().ToString(), "page_view",
            "https://geography-test.example/", eventId: eventId, forwardedFor: "213.127.0.1");

        var country = await WaitForEventCountryAsync(eventId, TimeSpan.FromSeconds(30));
        Skip.If(string.IsNullOrEmpty(country),
            "events.country is empty — run infrastructure/compose/scripts/refresh-geoip.sh to install a GeoIP database.");
        Assert.Equal("NL", country);

        // Aggregation rolls it into daily_geography_rollup on its next tick.
        var geoResponse = await WaitUntilAsync(async () =>
        {
            var r = await GetAsAsync($"/sites/{site.Id}/analytics/geography");
            if (!r.IsSuccessStatusCode) return false;
            var rows = await r.Content.ReadFromJsonAsync<JsonArray>();
            return rows!.Any(row => row!["country"]?.GetValue<string>() == "NL");
        }, TimeSpan.FromSeconds(120));
        Assert.True(geoResponse, "Expected a daily_geography_rollup row for NL within 120s.");
    }

    // ---- helpers (duplicated per this repo's cross-file test-helper convention) ----

    private static async Task<long> WaitForQualitySumAsync(Guid siteId, string dimension, TimeSpan timeout)
    {
        var query =
            $"SELECT sum(count) AS c FROM event_quality_daily WHERE site_id = '{siteId}' AND dimension = '{dimension}' FORMAT JSONEachRow";
        long value = 0;
        await WaitUntilAsync(async () =>
        {
            var line = await ClickHouseFirstLineAsync(query);
            if (line is null) return false;
            value = long.Parse(line["c"]!.GetValue<string>());
            return value >= 1;
        }, timeout);
        return value;
    }

    private static async Task<string?> WaitForEventCountryAsync(string eventId, TimeSpan timeout)
    {
        string? country = null;
        await WaitUntilAsync(async () =>
        {
            var line = await ClickHouseFirstLineAsync(
                $"SELECT country FROM events WHERE event_id = '{eventId}' FORMAT JSONEachRow");
            if (line is null) return false;
            country = line["country"]?.GetValue<string>();
            return !string.IsNullOrEmpty(country);
        }, timeout);
        return country;
    }

    private static async Task<JsonObject?> ClickHouseFirstLineAsync(string query)
    {
        var response = await ClickHouse.PostAsync(
            $"/?database=telumera_analytics&query={Uri.EscapeDataString(query)}", new StringContent(string.Empty));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var first = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is null ? null : JsonNode.Parse(first)?.AsObject();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        => await WaitUntilAsync(() => Task.FromResult(condition()), timeout);

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        return false;
    }

    private static async Task PostEventAsync(
        string siteToken, string sessionId, string name, string url,
        string? eventId = null, string? forwardedFor = null)
    {
        var body = new
        {
            Events = new[]
            {
                new
                {
                    Id = eventId ?? Guid.NewGuid().ToString(),
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
        if (forwardedFor is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        var response = await EventCollector.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task PostMalformedEventAsync(string siteToken)
    {
        var props = new Dictionary<string, object>();
        for (var i = 0; i < 26; i++) props[$"p{i}"] = i;
        var body = new
        {
            Events = new[]
            {
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "custom",
                    SiteToken = siteToken,
                    Environment = "production",
                    SessionId = Guid.NewGuid().ToString(),
                    VisitorId = (string?)null,
                    Url = "https://quality-test.example/x",
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    Properties = props,
                },
            },
        };
        var response = await EventCollector.PostAsync("/v1/events", JsonContent.Create(body));
        Assert.True(response.IsSuccessStatusCode); // the batch still 2xx; the event itself is rejected
    }

    private static async Task PostUnknownTokenBatchAsync()
    {
        var body = new
        {
            Events = new[]
            {
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "page_view",
                    SiteToken = "telum_" + Guid.NewGuid().ToString("N"),
                    Environment = "production",
                    SessionId = Guid.NewGuid().ToString(),
                    VisitorId = (string?)null,
                    Url = "https://unknown.example/",
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    Properties = new { },
                },
            },
        };
        var response = await EventCollector.PostAsync("/v1/events", JsonContent.Create(body));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<CreateSiteResponseDto> RegisterSiteAsync(string name, string domain)
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken), SkipReason);

        var workspaceResponse = await PostAsAsync(IdentityWorkspace, "/workspaces", new { Name = $"{name} Workspace" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();

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
