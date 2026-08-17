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

    [Fact]
    public async Task CreateWorkspace_ThenCreateSite_RoundTripsAndPublishesOutboxEvent()
    {
        var workspaceResponse = await IdentityWorkspace.PostAsJsonAsync("/workspaces", new { Name = "Test Workspace" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(workspace);

        var getWorkspaceResponse = await IdentityWorkspace.GetAsync($"/workspaces/{workspace!.Id}");
        getWorkspaceResponse.EnsureSuccessStatusCode();

        var siteResponse = await SiteRegistry.PostAsJsonAsync("/sites", new
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

        var getSiteResponse = await SiteRegistry.GetAsync($"/sites/{site.Id}");
        getSiteResponse.EnsureSuccessStatusCode();
        var fetchedSite = await getSiteResponse.Content.ReadFromJsonAsync<SiteDto>();
        Assert.Equal(site.BrowserToken, fetchedSite!.BrowserToken);

        var published = await WaitUntilOutboxEventPublishedAsync(site.Id, TimeSpan.FromSeconds(15));
        Assert.True(published, "Expected the site.created.v1 outbox row to be published within 15s.");
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
