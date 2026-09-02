using System.Text.Json;

using Dapr.AspNetCore;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

using Telumera.EventContracts;
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

// First real Redis consumer in the repo — see Telumera.Services.Analytics.Api.csproj's comment for why
// this is the standard IDistributedCache abstraction rather than a hand-rolled client.
var redisHost = builder.Configuration["Redis:Host"] ?? "localhost";
var redisPort = builder.Configuration["Redis:Port"] ?? "6379";
var redisPassword = builder.Configuration["Redis:Password"] ?? string.Empty;
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = $"{redisHost}:{redisPort},password={redisPassword}");

builder.Services.AddHttpClient();
builder.Services.AddSingleton<ClickHouseWriter>();
builder.Services.AddSingleton<ClickHouseQueryClient>();
builder.Services.AddSingleton<IGeoLookup, NoOpGeoLookup>();
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

app.UseAuthentication();
app.UseAuthorization();

app.MapAnalyticsQueryEndpoints();

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

app.MapSubscribeHandler();

app.Run();

public partial class Program;
