using System.Text.Json;

using Dapr.AspNetCore;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

using StackExchange.Redis;

using Telumera.EventContracts;
using Telumera.Idempotency;
using Telumera.ServiceDefaults;
using Telumera.Services.Analytics.Api;

var subscriptionJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Analytics")
    ?? throw new InvalidOperationException("ConnectionStrings:Analytics is not configured.");

builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddHealthChecks().AddNpgSql(connectionString, tags: ["ready"]);

// M01.6: the query endpoints are the first authenticated surface this service exposes — the existing
// /subscriptions/collector-events endpoint below stays unauthenticated (it simply omits
// .RequireAuthorization), same coexistence pattern site-registry's internal endpoints already use
// alongside its own protected ones.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddAuthorization(options => options.AddPolicy("ApiScope", policy =>
    policy.RequireClaim(ClaimConstants.Scope, "access_as_user")));

// M01.8: the live-visitors panel (components/analytics/LiveVisitorsPanel.vue) holds a WebSocket to
// LiveHub. A Dapr service-invocation forwarder can't proxy a WebSocket, so the dashboard connects to
// this service's origin directly — cross-origin in prod (dashboard at the apex, this at
// hubs.telumera.nl), hence a CORS policy this service didn't need before. AllowCredentials is
// required for the SignalR handshake, which rules out AllowAnyOrigin.
var corsAllowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddPolicy("LiveHub", policy =>
    policy.WithOrigins(corsAllowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// SignalR reads the bearer from the access_token query param on WebSocket/SSE transports (a browser
// WebSocket can't set an Authorization header). Scope that to /hubs so the header stays the source of
// truth everywhere else.
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events ??= new JwtBearerEvents();
    var next = options.Events.OnMessageReceived;
    options.Events.OnMessageReceived = async context =>
    {
        var accessToken = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
        }
        if (next is not null)
        {
            await next(context);
        }
    };
});

builder.Services.AddSignalR();

// First real Redis consumer in the repo — see Telumera.Services.Analytics.Api.csproj's comment for why
// the query cache uses the standard IDistributedCache abstraction rather than a hand-rolled client.
var redisHost = builder.Configuration["Redis:Host"] ?? "localhost";
var redisPort = builder.Configuration["Redis:Port"] ?? "6379";
var redisPassword = builder.Configuration["Redis:Password"] ?? string.Empty;
var redisConfiguration = $"{redisHost}:{redisPort},password={redisPassword}";
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConfiguration);

// The live-visitor projection needs Redis sorted-set / hash operations IDistributedCache can't
// express, so it talks to the same Redis through the raw StackExchange.Redis client.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConfiguration));
builder.Services.AddSingleton<LiveVisitorProjection>();
builder.Services.AddSingleton<LiveSubscriptions>();
builder.Services.AddHostedService<LiveBroadcastService>();

// M01.8 data-quality dashboard: subscribes to collector.quality.v1 (handler below) and probes the
// analytics dead-letter queue depth for the endpoint's "failed" figure.
builder.Services.AddSingleton<DeadLetterGauge>();
builder.Services.AddHostedService<DeadLetterProbe>();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<ClickHouseWriter>();
builder.Services.AddSingleton<ClickHouseQueryClient>();

// M01.8: use the local MMDB country database if it's mounted (prod / anyone who ran
// scripts/refresh-geoip.sh), otherwise degrade to no geography enrichment rather than failing to
// start — a fresh clone / CI has no .mmdb file and events must still flow. definitions §7: country
// granularity only, from an already-truncated IP that is never persisted.
var geoIpDatabasePath = builder.Configuration["GeoIp:DatabasePath"] ?? "/geoip/GeoLite2-Country.mmdb";
builder.Services.AddSingleton<IGeoLookup>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    if (File.Exists(geoIpDatabasePath))
    {
        logger.LogInformation("GeoIP: reading MMDB country database from {Path}.", geoIpDatabasePath);
        return new MmdbGeoLookup(geoIpDatabasePath);
    }

    logger.LogWarning(
        "GeoIP: no MMDB database at {Path} — geography enrichment disabled (events.country stays empty). "
        + "Run infrastructure/compose/scripts/refresh-geoip.sh to install one.", geoIpDatabasePath);
    return new NoOpGeoLookup();
});
builder.Services.AddSingleton<MembershipClient>();
builder.Services.AddSingleton<SiteLookupClient>();
builder.Services.AddSingleton<QueryCache>();
builder.Services.AddScoped<EventProcessor>();
builder.Services.AddHostedService<AnalyticsAggregationService>();

var app = builder.Build();

// No CI/migration-bundle pipeline exists yet (M00.5) — auto-migrate at startup so `docker compose up`
// stays self-sufficient for local development, same as every other Postgres-backed service.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>().Database.Migrate();
}

// ClickHouse has no EF Core migrations here — CREATE TABLE IF NOT EXISTS is the closest available
// equivalent, run once at startup the same way the Postgres side auto-migrates.
await app.Services.GetRequiredService<ClickHouseWriter>().EnsureSchemaAsync();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapAnalyticsQueryEndpoints();

// Live-visitors WebSocket — dashboard connects here directly (see LiveHub.cs). Its own CORS policy
// (AllowCredentials) and the /hubs access_token wire-up are set up above.
app.MapHub<LiveHub>("/hubs/live").RequireCors("LiveHub");

// Deliberately unauthenticated — reached only via Dapr pub/sub delivery from within the compose
// network, same as every subscription/internal endpoint elsewhere in this repo.
app.MapPost("/subscriptions/collector-events", async (HttpContext httpContext, EventProcessor processor, ILogger<Program> logger) =>
{
    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: httpContext.RequestAborted);
    var root = document.RootElement;
    var eventType = root.GetProperty("type").GetString();
    var cloudEventId = root.GetProperty("id").GetString();
    var data = root.GetProperty("data");

    if (eventType is not (EventTypes.AnalyticsPageViewReceivedV1 or EventTypes.AnalyticsCustomEventReceivedV1) || cloudEventId is null)
    {
        logger.LogDebug("Ignoring unrecognized event on collector-events: type={EventType}.", eventType);
        return Results.Ok();
    }

    var envelope = data.Deserialize<EventEnvelope<AnalyticsEventPayload>>(subscriptionJsonOptions);
    if (envelope is null)
    {
        logger.LogWarning("Could not deserialize collector-events payload for CloudEvent {EventId}.", cloudEventId);
        return Results.Ok(); // malformed, not retryable — ack to avoid an infinite redelivery loop
    }

    await processor.ProcessAsync(cloudEventId, eventType, envelope, httpContext.RequestAborted);
    return Results.Ok();
})
.WithTopic("pubsub", "collector-events")
.WithName("HandleCollectorEvents");

// M01.8: collector.quality.v1 delta batches → event_quality_daily (SummingMergeTree). Deduped on the
// CloudEvent id before applying, since Dapr is at-least-once and these deltas are additive — same
// marker-before-write pattern as EventProcessor.
app.MapPost("/subscriptions/quality-events", async (
    HttpContext httpContext, AnalyticsDbContext db, ClickHouseWriter clickHouse, ILogger<Program> logger) =>
{
    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: httpContext.RequestAborted);
    var root = document.RootElement;

    if (root.GetProperty("type").GetString() != EventTypes.CollectorQualityV1
        || root.GetProperty("id").GetString() is not { } cloudEventId
        || !Guid.TryParse(cloudEventId, out var eventId))
    {
        return Results.Ok();
    }

    var payload = root.GetProperty("data").Deserialize<CollectorQualityPayload>(subscriptionJsonOptions);
    if (payload is null || payload.Counts.Count == 0)
    {
        return Results.Ok();
    }

    if (!await db.TryBeginProcessingEventAsync(eventId, EventTypes.CollectorQualityV1, httpContext.RequestAborted))
    {
        return Results.Ok(); // already applied this delta batch
    }
    await db.SaveChangesAsync(httpContext.RequestAborted);

    var rows = payload.Counts.Select(c => new QualityDeltaRow(c.SiteId, payload.Date, c.Dimension, c.Count));
    await clickHouse.InsertQualityDeltasAsync(rows, httpContext.RequestAborted);
    logger.LogDebug("Applied {Count} collector.quality.v1 deltas for {Date}.", payload.Counts.Count, payload.Date);
    return Results.Ok();
})
.WithTopic("pubsub", "quality-events")
.WithName("HandleQualityEvents");

app.MapSubscribeHandler();

app.Run();

public partial class Program;
