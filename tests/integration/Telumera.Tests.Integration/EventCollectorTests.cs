using System.Net;
using System.Net.Http.Json;

namespace Telumera.Tests.Integration;

/// <summary>
/// Exercises event-collector against the real, already-running local Docker Compose stack (see
/// tests/integration/README.md) — requires `docker compose up -d` from infrastructure/compose/ first.
/// Covers the M01.3 vertical slice: register a site (via site-registry, requires a real Entra token —
/// same [SkippableFact] pattern as WorkspaceAndSiteFlowTests.cs), POST a batch of events through the
/// Collector's public, unauthenticated /v1/events endpoint, and confirm accept/reject/dedup behavior.
/// </summary>
public sealed class EventCollectorTests
{
    private static readonly HttpClient IdentityWorkspace = new() { BaseAddress = new("http://localhost:5101") };
    private static readonly HttpClient SiteRegistry = new() { BaseAddress = new("http://localhost:5102") };
    private static readonly HttpClient EventCollector = new() { BaseAddress = new("http://localhost:5103") };

    private static readonly string? AccessToken = Environment.GetEnvironmentVariable("TELUMERA_TEST_ACCESS_TOKEN");

    [SkippableFact]
    public async Task PostEvents_WithValidToken_ReturnsAccepted()
    {
        var site = await RegisterSiteAsync("Collector Accept Test", "collector-accept.example");

        var response = await EventCollector.PostAsJsonAsync("/v1/events", new
        {
            Events = new[] { SamplePageView(site.InitialToken, "https://collector-accept.example/") },
        });

        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CollectEventsResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(1, body!.Accepted);
        Assert.Equal(0, body.Rejected);
    }

    [SkippableFact]
    public async Task PostEvents_WithUnknownToken_ReturnsNotFound()
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken), SkipReason); // keeps this test grouped with the others even though it needs no real site

        var response = await EventCollector.PostAsJsonAsync("/v1/events", new
        {
            Events = new[] { SamplePageView("this-token-was-never-issued", "https://example.test/") },
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task PostEvents_WithDisallowedOrigin_ReturnsForbidden()
    {
        var site = await RegisterSiteAsync("Collector Origin Test", "collector-origin.example");

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/events")
        {
            Content = JsonContent.Create(new { Events = new[] { SamplePageView(site.InitialToken, "https://collector-origin.example/") } }),
        };
        request.Headers.Add("Origin", "https://not-the-registered-origin.example");

        var response = await EventCollector.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task PostEvents_SameEventIdTwice_SecondIsDeduplicated()
    {
        var site = await RegisterSiteAsync("Collector Dedup Test", "collector-dedup.example");
        var evt = SamplePageView(site.InitialToken, "https://collector-dedup.example/");

        var first = await EventCollector.PostAsJsonAsync("/v1/events", new { Events = new[] { evt } });
        first.EnsureSuccessStatusCode();

        var second = await EventCollector.PostAsJsonAsync("/v1/events", new { Events = new[] { evt } });
        second.EnsureSuccessStatusCode();

        // Both requests report the event as "accepted" (a retried duplicate isn't an error from the
        // client's perspective — see Program.cs), but only the first actually reaches the publish
        // channel. There's no outbox table to poll here (event-collector owns no persistent data), so
        // this test only proves the HTTP-level contract; confirming exactly-once publication requires
        // watching the collector-events Dapr topic directly (see services/event-collector/README.md).
        var secondBody = await second.Content.ReadFromJsonAsync<CollectEventsResponseDto>();
        Assert.NotNull(secondBody);
        Assert.Equal(1, secondBody!.Accepted);
    }

    [SkippableFact]
    public async Task PostEvents_MalformedBatch_ReturnsBadRequest()
    {
        var site = await RegisterSiteAsync("Collector Malformed Test", "collector-malformed.example");

        var oversizedProperties = Enumerable.Range(0, 30).ToDictionary(i => $"prop{i}", i => (object)i);
        var malformed = SamplePageView(site.InitialToken, "https://collector-malformed.example/") with { Properties = oversizedProperties };

        var response = await EventCollector.PostAsJsonAsync("/v1/events", new { Events = new[] { malformed } });

        response.EnsureSuccessStatusCode(); // the batch itself is accepted at the HTTP level...
        var body = await response.Content.ReadFromJsonAsync<CollectEventsResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(0, body!.Accepted); // ...but the one malformed event inside it is rejected, not the whole batch
        Assert.Equal(1, body.Rejected);
    }

    private static async Task<CreateSiteResponseDto> RegisterSiteAsync(string name, string domain)
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken), SkipReason);

        var workspaceResponse = await PostAsync(IdentityWorkspace, "/workspaces", new { Name = $"{name} Workspace" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(workspace);

        var siteResponse = await PostAsync(SiteRegistry, "/sites", new
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

    private static IncomingEventDto SamplePageView(string siteToken, string url) => new(
        Id: Guid.NewGuid().ToString(),
        Name: "page_view",
        SiteToken: siteToken,
        Environment: "production",
        SessionId: Guid.NewGuid().ToString(),
        VisitorId: null,
        Url: url,
        Timestamp: DateTimeOffset.UtcNow.ToString("O"),
        Properties: new Dictionary<string, object> { ["path"] = "/" });

    private static Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string path, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", AccessToken);
        return client.SendAsync(request);
    }

    private const string SkipReason =
        "TELUMERA_TEST_ACCESS_TOKEN not set — run infrastructure/compose/scripts/get-dev-token.sh " +
        "(or .ps1) and export the result to run this test (site registration still needs a real Entra " +
        "token, even though /v1/events itself doesn't).";

    private sealed record WorkspaceDto(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record CreateSiteResponseDto(
        Guid Id, Guid WorkspaceId, string Name, string CanonicalDomain, string[] AllowedOrigins,
        string Environment, DateTimeOffset CreatedAt, string InitialToken);

    private sealed record IncomingEventDto(
        string Id, string Name, string SiteToken, string? Environment, string SessionId,
        string? VisitorId, string Url, string Timestamp, Dictionary<string, object>? Properties);

    private sealed record CollectEventsResponseDto(int Accepted, int Rejected, string[]? Errors);
}
