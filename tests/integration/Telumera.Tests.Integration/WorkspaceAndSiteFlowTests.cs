using System.Net;
using System.Net.Http.Json;

using Npgsql;

namespace Telumera.Tests.Integration;

/// <summary>
/// Exercises identity-workspace and site-registry together against the real, already-running local
/// Docker Compose stack (see tests/integration/README.md) — requires `docker compose up -d` from
/// infrastructure/compose/ first. Covers the M00.4 vertical slice: register a workspace, register a
/// site under it, confirm the browser token comes back, and confirm the transactional outbox
/// (docs/adr/0004-transactional-outbox-and-idempotent-consumers.md) actually drains and publishes
/// site.created.v1.
/// </summary>
public sealed class WorkspaceAndSiteFlowTests
{
    private static readonly HttpClient IdentityWorkspace = new() { BaseAddress = new("http://localhost:5101") };
    private static readonly HttpClient SiteRegistry = new() { BaseAddress = new("http://localhost:5102") };

    // APP_DB_PASSWORD matches the same env var name infrastructure/compose/.env sets for the
    // per-context Postgres roles (docs/adr/0005) — run with that variable set to your local
    // .env value, or leave unset to fall back to .env.example's placeholder.
    private static readonly string SitesConnectionString =
        $"Host=localhost;Port=5432;Database=telumera_sites;Username=svc_sites;Password={Environment.GetEnvironmentVariable("APP_DB_PASSWORD") ?? "change-me-local-dev"}";

    // Both services require a valid Entra ID bearer token (access_as_user scope) since the auth
    // cut of M00.4. There's no automated way to complete an interactive device-code sign-in from
    // a test run, so this reads a token obtained ahead of time via
    // infrastructure/compose/scripts/get-dev-token.sh/.ps1 rather than acquiring one itself.
    private static readonly string? AccessToken = Environment.GetEnvironmentVariable("TELUMERA_TEST_ACCESS_TOKEN");

    [SkippableFact]
    public async Task CreateWorkspace_ThenCreateSite_RoundTripsAndPublishesOutboxEvent()
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken),
            "TELUMERA_TEST_ACCESS_TOKEN not set — run infrastructure/compose/scripts/get-dev-token.sh " +
            "(or .ps1) and export the result to run this test.");

        var workspaceResponse = await PostAsync(IdentityWorkspace, "/workspaces", new { Name = "Test Workspace" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(workspace);

        var getWorkspaceResponse = await GetAsync(IdentityWorkspace, $"/workspaces/{workspace!.Id}");
        getWorkspaceResponse.EnsureSuccessStatusCode();

        var siteResponse = await PostAsync(SiteRegistry, "/sites", new
        {
            WorkspaceId = workspace.Id,
            Name = "Test Site",
            CanonicalDomain = "example.test",
            AllowedOrigins = new[] { "https://example.test" },
            Environment = "production",
        });
        siteResponse.EnsureSuccessStatusCode();
        var site = await siteResponse.Content.ReadFromJsonAsync<SiteDto>();
        Assert.NotNull(site);
        Assert.False(string.IsNullOrWhiteSpace(site!.BrowserToken));

        var getSiteResponse = await GetAsync(SiteRegistry, $"/sites/{site.Id}");
        getSiteResponse.EnsureSuccessStatusCode();
        var fetchedSite = await getSiteResponse.Content.ReadFromJsonAsync<SiteDto>();
        Assert.Equal(site.BrowserToken, fetchedSite!.BrowserToken);

        var published = await WaitUntilOutboxEventPublishedAsync(site.Id, TimeSpan.FromSeconds(15));
        Assert.True(published, "Expected the site.created.v1 outbox row to be published within 15s.");
    }

    [Fact]
    public async Task ProtectedEndpoints_WithoutToken_Return401()
    {
        var createWorkspace = await IdentityWorkspace.PostAsJsonAsync("/workspaces", new { Name = "Should Be Rejected" });
        Assert.Equal(HttpStatusCode.Unauthorized, createWorkspace.StatusCode);

        var createSite = await SiteRegistry.PostAsJsonAsync("/sites", new
        {
            WorkspaceId = Guid.NewGuid(),
            Name = "Should Be Rejected",
            CanonicalDomain = "example.test",
            AllowedOrigins = new[] { "https://example.test" },
            Environment = "production",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, createSite.StatusCode);
    }

    private static Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string path, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", AccessToken);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", AccessToken);
        return client.SendAsync(request);
    }

    private static async Task<bool> WaitUntilOutboxEventPublishedAsync(Guid siteId, TimeSpan timeout)
    {
        await using var connection = new NpgsqlConnection(SitesConnectionString);
        await connection.OpenAsync();

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT published_at FROM outbox_events WHERE site_id = @siteId";
            command.Parameters.AddWithValue("siteId", siteId);

            var result = await command.ExecuteScalarAsync();
            if (result is not null and not DBNull)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return false;
    }

    private sealed record WorkspaceDto(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record SiteDto(
        Guid Id, Guid WorkspaceId, string Name, string CanonicalDomain,
        string[] AllowedOrigins, string Environment, string BrowserToken, DateTimeOffset CreatedAt);
}
