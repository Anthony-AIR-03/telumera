using System.Text.Json;
using System.Threading.RateLimiting;

using Dapr.AspNetCore;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Caching.Memory;

using Telumera.EventContracts;
using Telumera.ServiceDefaults;
using Telumera.Services.EventCollector.Api;

var subscriptionJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();
builder.Services.AddOpenApi();

// The SDK sends camelCase field names (packages/browser-sdk); case-insensitive matching makes those
// bind onto this service's PascalCase record properties without a separate naming-policy dependency.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNameCaseInsensitive = true);

// Payload limits (subtask: "Apply payload and rate limits") — generous enough for a batch of up to
// MaxBatchSize small JSON events, small enough to bound abuse.
builder.WebHost.ConfigureKestrel(serverOptions => serverOptions.Limits.MaxRequestBodySize = 262_144);

// Partitioned by client IP rather than siteToken: the token lives inside the JSON body, not a header,
// so IP-based partitioning avoids reading the request body twice. Revisit if per-site limiting turns
// out to matter more than per-IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("collector", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SiteRegistryClient>();
builder.Services.AddSingleton<SiteProjection>();
builder.Services.AddSingleton<DuplicateEventCache>();
builder.Services.AddSingleton<CollectorChannel>();
builder.Services.AddHostedService<CollectorPublisher>();
// M01.8: per-(site, outcome) tallies drained and published as collector.quality.v1 every 60s, feeding
// the analytics data-quality dashboard. In-memory + best-effort — see QualityCounters.cs.
builder.Services.AddSingleton<QualityCounters>();
builder.Services.AddHostedService<QualityRollupPublisher>();
// This service owns no persistent data (docs/architecture/bounded-contexts-and-data-ownership.md), so
// its site projection is rebuilt from site-registry on every startup (and periodically resynced)
// rather than restored from disk — see SiteProjectionSyncService.cs. Runs as a background service
// rather than a blocking startup call: the app and its own Dapr sidecar start concurrently, so a
// blocking first attempt commonly races ahead of the sidecar's HTTP port coming up.
builder.Services.AddHostedService<SiteProjectionSyncService>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRateLimiter();

// Deliberately no UseAuthentication/UseAuthorization — this is the one public/anonymous backend
// endpoint in the repo (docs/adr/0006-public-browser-ingestion-tokens.md); the browser site token is
// validated per-request against the SiteProjection instead of an Entra bearer token.
app.MapPost("/v1/events", async (
    HttpContext httpContext,
    CollectEventsRequest? request,
    SiteProjection projection,
    CollectorChannel channel,
    DuplicateEventCache duplicateCache,
    QualityCounters qualityCounters,
    IConfiguration configuration,
    IHostEnvironment env,
    ILogger<Program> logger) =>
{
    const int maxBatchSize = 100;

    if (request?.Events is not { Length: > 0 })
    {
        return Results.BadRequest("At least one event is required.");
    }
    if (request.Events.Length > maxBatchSize)
    {
        return Results.BadRequest($"A batch may contain at most {maxBatchSize} events.");
    }

    var siteToken = request.Events[0].SiteToken;
    if (request.Events.Any(e => e.SiteToken != siteToken))
    {
        return Results.BadRequest("All events in a batch must share the same siteToken.");
    }

    var site = await projection.ResolveAsync(siteToken, httpContext.RequestAborted);
    if (site is null)
    {
        qualityCounters.Record(Guid.Empty, QualityDimensions.RejectedUnknownToken, request.Events.Length);
        return Results.NotFound();
    }

    // Defense in depth (docs/adr/0006) — not the access-control boundary, so a missing Origin/Referer
    // is allowed through rather than rejected; a present-but-mismatched one is not.
    var origin = httpContext.Request.Headers.Origin.FirstOrDefault()
        ?? httpContext.Request.Headers.Referer.FirstOrDefault();
    if (origin is not null && !OriginMatches(origin, site.AllowedOrigins))
    {
        qualityCounters.Record(site.SiteId, QualityDimensions.RejectedOrigin, request.Events.Length);
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var truncatedIp = IpUtilities.Truncate(httpContext.Connection.RemoteIpAddress);
    var receivedAt = DateTimeOffset.UtcNow;
    var userAgent = httpContext.Request.Headers.UserAgent.ToString();
    var visitorHashSecret = configuration["Collector:VisitorHashSecret"] ?? "local-dev-secret-not-for-production";

    var accepted = 0;
    var rejected = 0;
    List<string>? errors = env.IsDevelopment() ? [] : null;

    foreach (var evt in request.Events)
    {
        var validationErrors = EventValidation.Validate(evt);
        if (validationErrors.Count > 0)
        {
            rejected++;
            qualityCounters.Record(site.SiteId, QualityDimensions.RejectedValidation);
            errors?.AddRange(validationErrors.Select(e => $"{evt.Id}: {e}"));
            continue;
        }

        // Every event type the SDK sends today (page_view, engagement, custom) belongs to Analytics —
        // Performance/Errors modules pertain to event categories no publisher emits yet.
        if (!site.EnabledModules.Contains("Analytics"))
        {
            rejected++;
            qualityCounters.Record(site.SiteId, QualityDimensions.RejectedModuleDisabled);
            continue;
        }

        if (!duplicateCache.TryClaim(evt.Id))
        {
            accepted++; // a retried duplicate is a success from the client's perspective, not an error
            qualityCounters.Record(site.SiteId, QualityDimensions.Duplicate);
            continue;
        }

        // Default mode (evt.VisitorId is null): compute the daily hash server-side (definitions doc
        // §3). Persistent mode (evt.VisitorId set by the SDK's opt-in config): pass it through as-is.
        var visitorId = evt.VisitorId
            ?? VisitorHash.Compute(visitorHashSecret, site.SiteId, truncatedIp, userAgent, receivedAt);

        var eventType = evt.Name == "page_view" ? EventTypes.AnalyticsPageViewReceivedV1 : EventTypes.AnalyticsCustomEventReceivedV1;
        var data = new AnalyticsEventPayload(evt.Name, evt.SessionId, visitorId, evt.Url, evt.Environment, evt.Properties, evt.Timestamp, receivedAt, truncatedIp, userAgent);
        var enrichedEvent = new EnrichedEvent(evt.Id, eventType, site.SiteId, site.WorkspaceId, httpContext.TraceIdentifier, receivedAt, data);

        if (!channel.TryEnqueue(enrichedEvent))
        {
            logger.LogWarning("Collector channel full; dropped event {EventId}.", evt.Id);
            qualityCounters.Record(site.SiteId, QualityDimensions.DroppedOverload);
        }
        else
        {
            qualityCounters.Record(site.SiteId, QualityDimensions.Accepted);
        }

        accepted++;
    }

    return Results.Accepted(value: new CollectEventsResponse(accepted, rejected, errors is { Count: > 0 } ? [.. errors] : null));
})
.WithName("CollectEvents")
.RequireRateLimiting("collector");

// Site Registry publishes site.created.v1/site.settings.changed.v1/site.key.rotated.v1 all onto the
// same "site-events" topic (see MembershipClient's sibling, SiteRegistryClient, and
// services/site-registry/README.md) — one handler dispatches on the CloudEvent's own `type` field
// rather than binding a single typed model, since the payload shape differs per type.
app.MapPost("/subscriptions/site-events", async (HttpContext httpContext, SiteProjection projection, ILogger<Program> logger) =>
{
    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: httpContext.RequestAborted);
    var root = document.RootElement;
    var eventType = root.GetProperty("type").GetString();
    var data = root.GetProperty("data");

    switch (eventType)
    {
        case EventTypes.SiteCreatedV1:
            if (data.Deserialize<EventEnvelope<SiteCreatedPayload>>(subscriptionJsonOptions) is { } created)
            {
                projection.ApplySiteCreated(created.Data.SiteId, created.Data.WorkspaceId, created.Data.AllowedOrigins);
            }
            break;

        case EventTypes.SiteSettingsChangedV1:
            if (data.Deserialize<EventEnvelope<SiteSettingsChangedPayload>>(subscriptionJsonOptions) is { } settingsChanged)
            {
                projection.ApplySettingsChanged(settingsChanged.Data.SiteId, settingsChanged.Data.Module, settingsChanged.Data.Enabled);
            }
            break;

        case EventTypes.SiteKeyRotatedV1:
            if (data.Deserialize<EventEnvelope<SiteKeyRotatedPayload>>(subscriptionJsonOptions) is { } keyRotated)
            {
                projection.ApplyKeyRotated(keyRotated.Data.SiteId, keyRotated.Data.Token, keyRotated.Data.Action);
            }
            break;

        default:
            logger.LogDebug("Ignoring unrecognized event type {EventType} on site-events.", eventType);
            break;
    }

    return Results.Ok();
})
.WithTopic("pubsub", "site-events")
.WithName("HandleSiteEvents");

app.MapSubscribeHandler();

app.Run();

static bool OriginMatches(string origin, string[] allowedOrigins)
{
    // Referer is a full URL; Origin is scheme+host[:port] only — normalize both before comparing.
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    var normalized = $"{uri.Scheme}://{uri.Authority}";
    return allowedOrigins.Any(a => string.Equals(a.TrimEnd('/'), normalized, StringComparison.OrdinalIgnoreCase));
}

internal sealed record IncomingEvent(
    string Id,
    string Name,
    string SiteToken,
    string? Environment,
    string SessionId,
    string? VisitorId,
    string Url,
    string Timestamp,
    Dictionary<string, JsonElement>? Properties);

internal sealed record CollectEventsRequest(IncomingEvent[] Events);

internal sealed record CollectEventsResponse(int Accepted, int Rejected, string[]? Errors);

/// <summary>
/// Event-specific payload carried in analytics.page-view.received.v1 / analytics.custom-event.received.v1's
/// EventEnvelope.Data. Carries raw TruncatedIp/UserAgent rather than a resolved country/device/browser —
/// that enrichment belongs to services/analytics (M01.4), not this "no analytics queries or heavy
/// processing" service.
/// </summary>
internal sealed record AnalyticsEventPayload(
    string Name, string SessionId, string? VisitorId, string Url, string? Environment,
    Dictionary<string, JsonElement>? Properties, string ClientTimestamp, DateTimeOffset ReceivedAt,
    string TruncatedIp, string UserAgent);

/// <summary>Consumer-side shape of site.created.v1's payload — deliberately duplicated from site-registry's own internal record (decoupled by design, same as any other event consumer).</summary>
internal sealed record SiteCreatedPayload(Guid SiteId, Guid WorkspaceId, string Name, string CanonicalDomain, string[] AllowedOrigins, string Environment);

/// <summary>Consumer-side shape of site.settings.changed.v1's payload.</summary>
internal sealed record SiteSettingsChangedPayload(Guid SiteId, string Module, bool Enabled);

/// <summary>Consumer-side shape of site.key.rotated.v1's payload.</summary>
internal sealed record SiteKeyRotatedPayload(Guid SiteId, Guid TokenId, string Token, string Action);

public partial class Program;
