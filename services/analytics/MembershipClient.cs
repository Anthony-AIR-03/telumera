using System.Text.Json;
using System.Text.Json.Serialization;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Checks a caller's role in a workspace by calling identity-workspace's internal membership endpoint
/// through the Dapr sidecar's service-invocation building block — copied verbatim from
/// services/site-registry/MembershipClient.cs (same "decoupled by design" duplication that codebase
/// already accepts for cross-service payload shapes, per docs/adr/0002-dapr-pubsub-abstraction.md).
/// </summary>
public sealed class MembershipClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    public async Task<Role?> GetRoleAsync(Guid workspaceId, string entraObjectId, CancellationToken cancellationToken = default)
    {
        var daprHttpPort = configuration["DAPR_HTTP_PORT"] ?? "3500";
        var url = $"http://localhost:{daprHttpPort}/v1.0/invoke/identity-workspace/method/internal/workspaces/{workspaceId}/members/{entraObjectId}";

        var httpClient = httpClientFactory.CreateClient(nameof(MembershipClient));
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Always 200 with a nullable Role — "not a member" is a normal outcome from this endpoint,
        // not an HTTP error (see the doc comment on the endpoint itself, services/identity-workspace/Program.cs).
        var result = await response.Content.ReadFromJsonAsync<MembershipResponse>(JsonOptions, cancellationToken);
        return result?.Role;
    }

    private sealed record MembershipResponse(Role? Role);
}
