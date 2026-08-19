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
        var site = await siteResponse.Content.ReadFromJsonAsync<CreateSiteResponseDto>();
        Assert.NotNull(site);
        Assert.False(string.IsNullOrWhiteSpace(site!.InitialToken));

        var getSiteResponse = await GetAsync(SiteRegistry, $"/sites/{site.Id}");
        getSiteResponse.EnsureSuccessStatusCode();

        var published = await WaitUntilOutboxEventPublishedAsync(site.Id, "site.created.v1", TimeSpan.FromSeconds(15));
        Assert.True(published, "Expected the site.created.v1 outbox row to be published within 15s.");
    }

    [SkippableFact]
    public async Task RotateSiteToken_KeepsOldTokenActive_ThenRevoke_MarksItRevoked()
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken),
            "TELUMERA_TEST_ACCESS_TOKEN not set — run infrastructure/compose/scripts/get-dev-token.sh " +
            "(or .ps1) and export the result to run this test.");

        var workspaceResponse = await PostAsync(IdentityWorkspace, "/workspaces", new { Name = "Key Rotation Test Workspace" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(workspace);

        var siteResponse = await PostAsync(SiteRegistry, "/sites", new
        {
            WorkspaceId = workspace!.Id,
            Name = "Key Rotation Test Site",
            CanonicalDomain = "rotation.example",
            AllowedOrigins = new[] { "https://rotation.example" },
            Environment = "production",
        });
        siteResponse.EnsureSuccessStatusCode();
        var site = await siteResponse.Content.ReadFromJsonAsync<CreateSiteResponseDto>();
        Assert.NotNull(site);

        // Rotating must not touch the existing token — both stay valid ("overlapping keys").
        var rotateResponse = await PostAsync<object?>(SiteRegistry, $"/sites/{site!.Id}/tokens/rotate", null);
        rotateResponse.EnsureSuccessStatusCode();
        var newToken = await rotateResponse.Content.ReadFromJsonAsync<SiteTokenDto>();
        Assert.NotNull(newToken);

        var afterRotate = await GetAsync(SiteRegistry, $"/sites/{site.Id}/tokens");
        afterRotate.EnsureSuccessStatusCode();
        var tokensAfterRotate = await afterRotate.Content.ReadFromJsonAsync<List<SiteTokenDto>>();
        Assert.NotNull(tokensAfterRotate);
        Assert.Equal(2, tokensAfterRotate!.Count);
        Assert.All(tokensAfterRotate, t => Assert.Null(t.RevokedAt));

        var initialToken = tokensAfterRotate.Single(t => t.Token == site.InitialToken);

        var revokeResponse = await PostAsync<object?>(SiteRegistry, $"/sites/{site.Id}/tokens/{initialToken.Id}/revoke", null);
        revokeResponse.EnsureSuccessStatusCode();

        var afterRevoke = await GetAsync(SiteRegistry, $"/sites/{site.Id}/tokens");
        afterRevoke.EnsureSuccessStatusCode();
        var tokensAfterRevoke = await afterRevoke.Content.ReadFromJsonAsync<List<SiteTokenDto>>();
        Assert.NotNull(tokensAfterRevoke);
        Assert.NotNull(tokensAfterRevoke!.Single(t => t.Id == initialToken.Id).RevokedAt);
        Assert.Null(tokensAfterRevoke.Single(t => t.Id == newToken!.Id).RevokedAt);

        var rotatedPublished = await WaitUntilOutboxEventPublishedAsync(site.Id, "site.key.rotated.v1", TimeSpan.FromSeconds(15));
        Assert.True(rotatedPublished, "Expected at least one site.key.rotated.v1 outbox row to be published within 15s.");
    }

    [SkippableFact]
    public async Task CreateSite_UnderWorkspaceCallerIsNotMemberOf_Returns403()
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken),
            "TELUMERA_TEST_ACCESS_TOKEN not set — run infrastructure/compose/scripts/get-dev-token.sh " +
            "(or .ps1) and export the result to run this test.");

        // A freshly-generated, never-created WorkspaceId — the signed-in test user has no
        // membership in it (proves enforcement without needing a second real Entra identity).
        var siteResponse = await PostAsync(SiteRegistry, "/sites", new
        {
            WorkspaceId = Guid.NewGuid(),
            Name = "Should Be Rejected",
            CanonicalDomain = "example.test",
            AllowedOrigins = new[] { "https://example.test" },
            Environment = "production",
        });

        Assert.Equal(HttpStatusCode.Forbidden, siteResponse.StatusCode);
    }

    [SkippableFact]
    public async Task AddWorkspaceMember_ThenListMembers_RoundTrips()
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken),
            "TELUMERA_TEST_ACCESS_TOKEN not set — run infrastructure/compose/scripts/get-dev-token.sh " +
            "(or .ps1) and export the result to run this test.");

        var workspaceResponse = await PostAsync(IdentityWorkspace, "/workspaces", new { Name = "Membership Test Workspace" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(workspace);

        // Not a real, signable-in identity — just proves the add/list mechanics work. See the
        // verification notes in the plan for why role *enforcement* for this specific member can't
        // be exercised without a second real Entra identity.
        var fakeMemberObjectId = Guid.NewGuid().ToString();
        var addMemberResponse = await PostAsync(IdentityWorkspace, $"/workspaces/{workspace!.Id}/members",
            new { EntraObjectId = fakeMemberObjectId, Role = "Viewer" });
        addMemberResponse.EnsureSuccessStatusCode();

        var membersResponse = await GetAsync(IdentityWorkspace, $"/workspaces/{workspace.Id}/members");
        membersResponse.EnsureSuccessStatusCode();
        var members = await membersResponse.Content.ReadFromJsonAsync<List<MemberDto>>();

        Assert.NotNull(members);
        Assert.Equal(2, members!.Count);
        Assert.Contains(members, m => m.EntraObjectId == fakeMemberObjectId && m.Role == "Viewer");
        Assert.Contains(members, m => m.Role == "Owner");
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

    private static async Task<bool> WaitUntilOutboxEventPublishedAsync(Guid siteId, string eventType, TimeSpan timeout)
    {
        await using var connection = new NpgsqlConnection(SitesConnectionString);
        await connection.OpenAsync();

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT published_at FROM outbox_events WHERE site_id = @siteId AND event_type = @eventType " +
                "AND published_at IS NOT NULL LIMIT 1";
            command.Parameters.AddWithValue("siteId", siteId);
            command.Parameters.AddWithValue("eventType", eventType);

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

    private sealed record MemberDto(string EntraObjectId, string? DisplayName, string? Email, string Role);

    private sealed record CreateSiteResponseDto(
        Guid Id, Guid WorkspaceId, string Name, string CanonicalDomain, string[] AllowedOrigins,
        string Environment, DateTimeOffset CreatedAt, string InitialToken);

    private sealed record SiteTokenDto(Guid Id, string Token, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);
}
