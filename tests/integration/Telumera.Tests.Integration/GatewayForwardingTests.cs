using System.Net;
using System.Net.Http.Json;

namespace Telumera.Tests.Integration;

/// <summary>
/// Proves gateway/'s pass-through forwarding actually works end to end (see
/// gateway/GatewayForwarder.cs) — not a full duplicate of every identity-workspace/site-registry
/// test through the gateway, since the services' own tests already cover their business logic. One
/// round trip through the gateway's URLs is enough to prove the forwarder itself is correct.
/// </summary>
public sealed class GatewayForwardingTests
{
    private static readonly HttpClient Gateway = new() { BaseAddress = new("http://localhost:5100") };

    private static readonly string? AccessToken = Environment.GetEnvironmentVariable("TELUMERA_TEST_ACCESS_TOKEN");

    [SkippableFact]
    public async Task CreateWorkspace_ThenCreateSite_ThroughGateway_RoundTrips()
    {
        Skip.If(string.IsNullOrWhiteSpace(AccessToken),
            "TELUMERA_TEST_ACCESS_TOKEN not set — run infrastructure/compose/scripts/get-dev-token.sh " +
            "(or .ps1) and export the result to run this test.");

        var workspaceResponse = await PostAsync("/workspaces", new { Name = "Gateway Test Workspace" });
        workspaceResponse.EnsureSuccessStatusCode();
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(workspace);

        var getWorkspaceResponse = await GetAsync($"/workspaces/{workspace!.Id}");
        getWorkspaceResponse.EnsureSuccessStatusCode();

        var siteResponse = await PostAsync("/sites", new
        {
            WorkspaceId = workspace.Id,
            Name = "Gateway Test Site",
            CanonicalDomain = "gateway.example",
            AllowedOrigins = new[] { "https://gateway.example" },
            Environment = "production",
        });
        siteResponse.EnsureSuccessStatusCode();
        var site = await siteResponse.Content.ReadFromJsonAsync<CreateSiteResponseDto>();
        Assert.NotNull(site);
        Assert.False(string.IsNullOrWhiteSpace(site!.InitialToken));

        var getSiteResponse = await GetAsync($"/sites/{site.Id}");
        getSiteResponse.EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task UnknownPathPrefix_Returns404()
    {
        // Authorization runs in middleware before the endpoint handler, so a valid token is needed
        // to actually reach GatewayForwarder's prefix check rather than 401-ing first.
        Skip.If(string.IsNullOrWhiteSpace(AccessToken),
            "TELUMERA_TEST_ACCESS_TOKEN not set — run infrastructure/compose/scripts/get-dev-token.sh " +
            "(or .ps1) and export the result to run this test.");

        var response = await GetAsync("/nonsense");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequest_Returns401()
    {
        var response = await Gateway.GetAsync("/workspaces");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostAsync<T>(string path, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", AccessToken);
        return Gateway.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", AccessToken);
        return Gateway.SendAsync(request);
    }

    private sealed record WorkspaceDto(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record CreateSiteResponseDto(
        Guid Id, Guid WorkspaceId, string Name, string CanonicalDomain, string[] AllowedOrigins,
        string Environment, DateTimeOffset CreatedAt, string InitialToken);
}
