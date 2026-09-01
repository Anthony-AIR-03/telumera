using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Telumera.Services.EventCollector.Api;

/// <summary>
/// Calls site-registry's internal token endpoints through the Dapr sidecar's service-invocation
/// building block, per docs/adr/0002-dapr-pubsub-abstraction.md — same pattern as
/// services/site-registry/MembershipClient.cs calling identity-workspace. Used for the Collector's
/// projection warm-up/full-resync (<see cref="ListActiveTokensAsync"/>) and the cache-miss fallback
/// (<see cref="LookupTokenAsync"/>) — see docs/architecture/c4-container.md's note that a live call is
/// only for cache miss/warm-up, not the steady-state per-event path.
/// </summary>
public sealed class SiteRegistryClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private string BaseUrl => $"http://localhost:{configuration["DAPR_HTTP_PORT"] ?? "3500"}/v1.0/invoke/site-registry/method";

    public async Task<SiteTokenLookup?> LookupTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var httpClient = httpClientFactory.CreateClient(nameof(SiteRegistryClient));
        using var response = await httpClient.GetAsync($"{BaseUrl}/internal/tokens/{Uri.EscapeDataString(token)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        // Always 200 with Found: false for an unknown/revoked token — not an HTTP error, same
        // rationale as identity-workspace's internal membership endpoint.
        var result = await response.Content.ReadFromJsonAsync<InternalTokenLookupResponse>(JsonOptions, cancellationToken);
        return result is { Found: true } ? result.ToLookup() : null;
    }

    public async Task<IReadOnlyList<SiteTokenLookup>> ListActiveTokensAsync(CancellationToken cancellationToken = default)
    {
        var httpClient = httpClientFactory.CreateClient(nameof(SiteRegistryClient));
        using var response = await httpClient.GetAsync($"{BaseUrl}/internal/tokens", cancellationToken);
        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<InternalTokenListEntry>>(JsonOptions, cancellationToken)
            ?? [];
        return entries.Select(e => new SiteTokenLookup(e.Token, e.SiteId, e.WorkspaceId, e.AllowedOrigins, e.EnabledModules)).ToList();
    }

    private sealed record InternalTokenLookupResponse(bool Found, Guid? SiteId, Guid? WorkspaceId, string[]? AllowedOrigins, string[]? EnabledModules)
    {
        public SiteTokenLookup ToLookup() => new(
            Token: string.Empty, // caller already knows the token it looked up
            SiteId: SiteId!.Value,
            WorkspaceId: WorkspaceId!.Value,
            AllowedOrigins: AllowedOrigins ?? [],
            EnabledModules: EnabledModules ?? []);
    }

    private sealed record InternalTokenListEntry(string Token, Guid SiteId, Guid WorkspaceId, string[] AllowedOrigins, string[] EnabledModules);
}

/// <summary>A resolved site token's projection data — see <see cref="SiteProjection"/> for the in-memory cache built from this shape.</summary>
public sealed record SiteTokenLookup(string Token, Guid SiteId, Guid WorkspaceId, string[] AllowedOrigins, string[] EnabledModules);
