using System.Text.Json;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Resolves a siteId to its owning workspaceId by calling site-registry's internal lookup endpoint
/// through Dapr service invocation — same shape as MembershipClient.cs and
/// services/site-registry/MembershipClient.cs. Analytics has no local site->workspace projection (see
/// services/analytics/README.md's M01.6 section for why a SiteProjection-style cache isn't the right
/// fit here), so every query endpoint calls this directly rather than maintaining one.
/// </summary>
public sealed class SiteLookupClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Guid?> GetWorkspaceIdAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var daprHttpPort = configuration["DAPR_HTTP_PORT"] ?? "3500";
        var url = $"http://localhost:{daprHttpPort}/v1.0/invoke/site-registry/method/internal/sites/{siteId}";

        var httpClient = httpClientFactory.CreateClient(nameof(SiteLookupClient));
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Always 200 with Found:false for an unknown site — same "not found is a normal outcome, not
        // an HTTP error" convention as every other /internal/* endpoint in this repo.
        var result = await response.Content.ReadFromJsonAsync<SiteLookupResponse>(JsonOptions, cancellationToken);
        return result is { Found: true } ? result.WorkspaceId : null;
    }

    private sealed record SiteLookupResponse(bool Found, Guid? WorkspaceId);
}
