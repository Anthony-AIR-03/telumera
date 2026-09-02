namespace Telumera.Gateway;

/// <summary>
/// Transparent reverse-proxy forwarder: same paths, same request/response shapes as the underlying
/// services, routed via Dapr service invocation
/// (docs/adr/0002-dapr-pubsub-abstraction.md — "synchronous cross-service calls ... use Dapr service
/// invocation rather than hardcoded service URLs"), same pattern
/// services/site-registry/MembershipClient.cs already established.
///
/// The caller's own bearer token is forwarded unchanged — the gateway validates it itself (see
/// Program.cs's ApiScope policy) and each downstream service independently validates it again and
/// runs its own membership checks exactly as it does when called directly. No new
/// gateway-to-service trust model.
///
/// One route per top-level path segment, not one per existing endpoint — avoids re-duplicating every
/// endpoint signature here as identity-workspace/site-registry grow. Real response-composition
/// endpoints (aggregating multiple services into one call) are added later, once an actual dashboard
/// screen needs one (see gateway/README.md).
/// </summary>
public sealed class GatewayForwarder(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private static readonly Dictionary<string, string> RouteToAppId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["workspaces"] = "identity-workspace",
        ["sites"] = "site-registry",
    };

    public async Task ForwardAsync(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value ?? string.Empty;
        var segments = path.TrimStart('/').Split('/');
        var firstSegment = segments[0];

        // GET /workspaces/{id}/sites is site-registry's endpoint (a workspace's sites, not one of
        // identity-workspace's own resources) — carve this one path out of the otherwise-uniform
        // per-top-level-segment table rather than teaching the table about sub-resources.
        string? appId;
        if (firstSegment.Equals("workspaces", StringComparison.OrdinalIgnoreCase)
            && segments.Length == 3
            && segments[2].Equals("sites", StringComparison.OrdinalIgnoreCase))
        {
            appId = "site-registry";
        }
        // Same reasoning, M01.6: /sites/{id}/analytics/** is the Analytics Service's query API, not one
        // of site-registry's own resources — without this carve-out the table's ["sites"] entry would
        // misroute every one of these seven endpoints to site-registry.
        else if (firstSegment.Equals("sites", StringComparison.OrdinalIgnoreCase)
            && segments.Length >= 3
            && segments[2].Equals("analytics", StringComparison.OrdinalIgnoreCase))
        {
            appId = "analytics";
        }
        else if (!RouteToAppId.TryGetValue(firstSegment, out appId))
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var daprHttpPort = configuration["DAPR_HTTP_PORT"] ?? "3500";
        var targetUrl = $"http://localhost:{daprHttpPort}/v1.0/invoke/{appId}/method{path}{httpContext.Request.QueryString}";

        using var request = new HttpRequestMessage(new HttpMethod(httpContext.Request.Method), targetUrl);

        var method = httpContext.Request.Method;
        if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method))
        {
            request.Content = new StreamContent(httpContext.Request.Body);
            if (!string.IsNullOrEmpty(httpContext.Request.ContentType))
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", httpContext.Request.ContentType);
            }
        }

        if (httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", (string?)authHeader);
        }

        var httpClient = httpClientFactory.CreateClient(nameof(GatewayForwarder));
        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, httpContext.RequestAborted);

        httpContext.Response.StatusCode = (int)response.StatusCode;
        if (response.Content.Headers.ContentType is not null)
        {
            httpContext.Response.ContentType = response.Content.Headers.ContentType.ToString();
        }

        await response.Content.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
    }
}
