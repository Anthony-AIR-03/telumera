using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

using Telumera.EventContracts;
using Telumera.Outbox;
using Telumera.ServiceDefaults;
using Telumera.Services.SiteRegistry.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();
builder.Services.AddOpenApi();

// Lets Module serialize/deserialize as "Analytics"/"Performance"/"Errors" instead of a raw int.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Outbox payloads are serialized outside the ASP.NET Core request pipeline, so they don't pick up
// ConfigureHttpJsonOptions automatically — anything with an enum field needs this passed explicitly.
var outboxJsonOptions = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Requires the access_as_user delegated scope (docs/adr — Telumera API app registration's "Expose an
// API" page) rather than a fallback policy, so /health/live and /health/ready (MapDefaultEndpoints,
// packages/dotnet-service-defaults) stay open for container healthchecks.
builder.Services.AddAuthorization(options => options.AddPolicy("ApiScope", policy =>
    policy.RequireClaim(ClaimConstants.Scope, "access_as_user")));

var connectionString = builder.Configuration.GetConnectionString("SiteRegistry")
    ?? throw new InvalidOperationException("ConnectionStrings:SiteRegistry is not configured.");

builder.Services.AddDbContext<SiteRegistryDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddHealthChecks().AddNpgSql(connectionString, tags: ["ready"]);
builder.Services.AddHttpClient();
builder.Services.AddOutboxPublisher<SiteRegistryDbContext>(topic: "site-events", source: "telumera.site-registry");
builder.Services.AddSingleton<MembershipClient>();

var app = builder.Build();

// No CI/migration-bundle pipeline exists yet (M00.5) — auto-migrate at startup so
// `docker compose up` stays self-sufficient for local development.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<SiteRegistryDbContext>().Database.Migrate();
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// WorkspaceId is checked against identity-workspace's membership records (via Dapr service
// invocation, see MembershipClient) — the caller must be Developer+ in the target workspace.
app.MapPost("/sites", async (CreateSiteRequest request, SiteRegistryDbContext db, HttpContext httpContext, MembershipClient membershipClient) =>
{
    var context = new ValidationContext(request);
    var errors = new List<ValidationResult>();

    if (!Validator.TryValidateObject(request, context, errors, validateAllProperties: true))
    {
        var problemErrors = errors
            .SelectMany(e => e.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => (member, e.ErrorMessage ?? "Invalid value.")))
            .GroupBy(e => e.member)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Item2).ToArray());

        return Results.ValidationProblem(problemErrors);
    }

    var callerObjectId = httpContext.User.GetObjectId()
        ?? throw new InvalidOperationException("Token has no oid claim.");
    var role = await membershipClient.GetRoleAsync(request.WorkspaceId, callerObjectId);
    if (role is null || role < Role.Developer)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var site = new Site
    {
        Id = Guid.NewGuid(),
        WorkspaceId = request.WorkspaceId,
        Name = request.Name,
        CanonicalDomain = request.CanonicalDomain,
        AllowedOrigins = request.AllowedOrigins,
        Environment = request.Environment,
        CreatedAt = DateTimeOffset.UtcNow,
    };
    db.Sites.Add(site);

    var initialToken = new SiteToken
    {
        Id = Guid.NewGuid(),
        SiteId = site.Id,
        Token = GenerateSiteToken(),
        CreatedAt = site.CreatedAt,
    };
    db.SiteTokens.Add(initialToken);

    // New tracking installs start with every module on; site owners opt out of specific ones
    // rather than opting in (docs/architecture/bounded-contexts-and-data-ownership.md — Site
    // Registry owns "settings, enabled-module flags").
    foreach (var module in Enum.GetValues<Module>())
    {
        db.SiteModuleSettings.Add(new SiteModuleSetting
        {
            Id = Guid.NewGuid(),
            SiteId = site.Id,
            Module = module,
            Enabled = true,
            UpdatedAt = site.CreatedAt,
        });
    }

    var eventData = new SiteCreatedEventData(
        site.Id, site.WorkspaceId, site.Name, site.CanonicalDomain, site.AllowedOrigins, site.Environment);

    db.OutboxEvents.Add(new OutboxEvent
    {
        Id = Guid.NewGuid(),
        EventType = EventTypes.SiteCreatedV1,
        TenantId = site.WorkspaceId,
        SiteId = site.Id,
        CorrelationId = CurrentCorrelationId(),
        DataJson = JsonSerializer.Serialize(eventData),
        CreatedAt = site.CreatedAt,
    });

    // Single SaveChangesAsync call = single transaction: the Site row, its initial SiteToken, its
    // default SiteModuleSettings, and its OutboxEvent row commit together or not at all
    // (docs/adr/0004-transactional-outbox-and-idempotent-consumers.md).
    await db.SaveChangesAsync();

    return Results.Created($"/sites/{site.Id}", new CreateSiteResponse(
        site.Id, site.WorkspaceId, site.Name, site.CanonicalDomain, site.AllowedOrigins,
        site.Environment, site.CreatedAt, initialToken.Token));
})
.WithName("CreateSite")
.RequireAuthorization("ApiScope");

// Lists sites for a workspace — Viewer+ membership check, same pattern as every other endpoint here.
app.MapGet("/workspaces/{workspaceId:guid}/sites", async (Guid workspaceId, SiteRegistryDbContext db, HttpContext httpContext, MembershipClient membershipClient) =>
{
    var callerObjectId = httpContext.User.GetObjectId()
        ?? throw new InvalidOperationException("Token has no oid claim.");
    var role = await membershipClient.GetRoleAsync(workspaceId, callerObjectId);
    if (role is null)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var sites = await db.Sites
        .Where(s => s.WorkspaceId == workspaceId)
        .Select(s => new SiteSummaryDto(s.Id, s.Name, s.CanonicalDomain, s.Environment, s.CreatedAt))
        .ToListAsync();

    return Results.Ok(sites);
})
.WithName("ListWorkspaceSites")
.RequireAuthorization("ApiScope");

app.MapGet("/sites/{id:guid}", async (Guid id, SiteRegistryDbContext db, HttpContext httpContext, MembershipClient membershipClient) =>
{
    var site = await db.Sites.FindAsync(id);
    if (site is null)
    {
        return Results.NotFound();
    }

    var callerObjectId = httpContext.User.GetObjectId()
        ?? throw new InvalidOperationException("Token has no oid claim.");
    var role = await membershipClient.GetRoleAsync(site.WorkspaceId, callerObjectId);
    if (role is null)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    return Results.Ok(new SiteDetailDto(site.Id, site.WorkspaceId, site.Name, site.CanonicalDomain, site.Environment, site.CreatedAt, role.Value));
})
.WithName("GetSite")
.RequireAuthorization("ApiScope");

// Tokens are a sub-resource of a site — see SiteToken.cs and services/site-registry/README.md for
// why rotation (issuing a new one) never touches existing tokens ("overlapping keys during safe
// migration") and revocation is a separate, explicit action.
app.MapGet("/sites/{id:guid}/tokens", async (Guid id, SiteRegistryDbContext db, HttpContext httpContext, MembershipClient membershipClient) =>
{
    var site = await db.Sites.FindAsync(id);
    if (site is null)
    {
        return Results.NotFound();
    }

    var callerObjectId = httpContext.User.GetObjectId()
        ?? throw new InvalidOperationException("Token has no oid claim.");
    var role = await membershipClient.GetRoleAsync(site.WorkspaceId, callerObjectId);
    if (role is null)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var tokens = await db.SiteTokens
        .Where(t => t.SiteId == id)
        .OrderBy(t => t.CreatedAt)
        .Select(t => new SiteTokenDto(t.Id, t.Token, t.CreatedAt, t.RevokedAt))
        .ToListAsync();

    return Results.Ok(tokens);
})
.WithName("GetSiteTokens")
.RequireAuthorization("ApiScope");

app.MapPost("/sites/{id:guid}/tokens/rotate", async (Guid id, SiteRegistryDbContext db, HttpContext httpContext, MembershipClient membershipClient) =>
{
    var site = await db.Sites.FindAsync(id);
    if (site is null)
    {
        return Results.NotFound();
    }

    var callerObjectId = httpContext.User.GetObjectId()
        ?? throw new InvalidOperationException("Token has no oid claim.");
    var role = await membershipClient.GetRoleAsync(site.WorkspaceId, callerObjectId);
    if (role is null || role < Role.Developer)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var newToken = new SiteToken
    {
        Id = Guid.NewGuid(),
        SiteId = id,
        Token = GenerateSiteToken(),
        CreatedAt = DateTimeOffset.UtcNow,
    };
    db.SiteTokens.Add(newToken);

    AddSiteKeyRotatedOutboxEvent(db, site, newToken, "issued");

    await db.SaveChangesAsync();

    return Results.Created($"/sites/{id}/tokens/{newToken.Id}",
        new SiteTokenDto(newToken.Id, newToken.Token, newToken.CreatedAt, newToken.RevokedAt));
})
.WithName("RotateSiteToken")
.RequireAuthorization("ApiScope");

app.MapPost("/sites/{id:guid}/tokens/{tokenId:guid}/revoke", async (Guid id, Guid tokenId, SiteRegistryDbContext db, HttpContext httpContext, MembershipClient membershipClient) =>
{
    var site = await db.Sites.FindAsync(id);
    if (site is null)
    {
        return Results.NotFound();
    }

    var callerObjectId = httpContext.User.GetObjectId()
        ?? throw new InvalidOperationException("Token has no oid claim.");
    var role = await membershipClient.GetRoleAsync(site.WorkspaceId, callerObjectId);
    if (role is null || role < Role.Developer)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var token = await db.SiteTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.SiteId == id);
    if (token is null)
    {
        return Results.NotFound();
    }

    if (token.RevokedAt is null)
    {
        token.RevokedAt = DateTimeOffset.UtcNow;
        AddSiteKeyRotatedOutboxEvent(db, site, token, "revoked");
        await db.SaveChangesAsync();
    }

    return Results.Ok(new SiteTokenDto(token.Id, token.Token, token.CreatedAt, token.RevokedAt));
})
.WithName("RevokeSiteToken")
.RequireAuthorization("ApiScope");

// Module settings are a sub-resource of a site, created once (all enabled) at registration — see
// the foreach loop in POST /sites and services/site-registry/README.md.
app.MapGet("/sites/{id:guid}/modules", async (Guid id, SiteRegistryDbContext db, HttpContext httpContext, MembershipClient membershipClient) =>
{
    var site = await db.Sites.FindAsync(id);
    if (site is null)
    {
        return Results.NotFound();
    }

    var callerObjectId = httpContext.User.GetObjectId()
        ?? throw new InvalidOperationException("Token has no oid claim.");
    var role = await membershipClient.GetRoleAsync(site.WorkspaceId, callerObjectId);
    if (role is null)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var settings = await db.SiteModuleSettings
        .Where(s => s.SiteId == id)
        .Select(s => new SiteModuleSettingDto(s.Module, s.Enabled, s.UpdatedAt))
        .ToListAsync();

    return Results.Ok(settings);
})
.WithName("GetSiteModules")
.RequireAuthorization("ApiScope");

app.MapPatch("/sites/{id:guid}/modules/{module}", async (Guid id, string module, UpdateModuleSettingRequest request, SiteRegistryDbContext db, HttpContext httpContext, MembershipClient membershipClient) =>
{
    if (!Enum.TryParse<Module>(module, ignoreCase: true, out var parsedModule))
    {
        return Results.Problem($"Unknown module '{module}'.", statusCode: StatusCodes.Status400BadRequest);
    }

    var site = await db.Sites.FindAsync(id);
    if (site is null)
    {
        return Results.NotFound();
    }

    var callerObjectId = httpContext.User.GetObjectId()
        ?? throw new InvalidOperationException("Token has no oid claim.");
    var role = await membershipClient.GetRoleAsync(site.WorkspaceId, callerObjectId);
    if (role is null || role < Role.Developer)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var setting = await db.SiteModuleSettings.FirstAsync(s => s.SiteId == id && s.Module == parsedModule);

    if (setting.Enabled != request.Enabled)
    {
        setting.Enabled = request.Enabled;
        setting.UpdatedAt = DateTimeOffset.UtcNow;

        var eventData = new SiteSettingsChangedEventData(site.Id, parsedModule, request.Enabled);
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            EventType = EventTypes.SiteSettingsChangedV1,
            TenantId = site.WorkspaceId,
            SiteId = site.Id,
            CorrelationId = CurrentCorrelationId(),
            // Explicit converter: Module must serialize as "Analytics"/etc, not a raw int — the
            // default JsonSerializer.Serialize() call doesn't pick up the app's ConfigureHttpJsonOptions.
            DataJson = JsonSerializer.Serialize(eventData, outboxJsonOptions),
            CreatedAt = setting.UpdatedAt,
        });

        await db.SaveChangesAsync();
    }

    return Results.Ok(new SiteModuleSettingDto(setting.Module, setting.Enabled, setting.UpdatedAt));
})
.WithName("UpdateSiteModule")
.RequireAuthorization("ApiScope");

// Deliberately unauthenticated at the HTTP level, same rationale and pattern as identity-workspace's
// GetInternalMembership: reached only via Dapr service invocation from within the compose network
// (see event-collector's SiteRegistryClient), trusting the network boundary since no
// service-to-service auth scheme exists yet. Always 200, never 404 — "unknown/revoked token" is a
// normal outcome for the Collector to handle (reject the request), not a failure worth Dapr's
// resiliency retry/circuit-breaker treating it as one.
app.MapGet("/internal/tokens/{token}", async (string token, SiteRegistryDbContext db) =>
{
    var tokenRow = await db.SiteTokens.FirstOrDefaultAsync(t => t.Token == token && t.RevokedAt == null);
    if (tokenRow is null)
    {
        return Results.Ok(InternalTokenLookupResponse.NotFound);
    }

    var site = await db.Sites.FindAsync(tokenRow.SiteId);
    if (site is null)
    {
        return Results.Ok(InternalTokenLookupResponse.NotFound);
    }

    var enabledModules = await GetEnabledModuleNamesAsync(db, site.Id);

    return Results.Ok(new InternalTokenLookupResponse(true, site.Id, site.WorkspaceId, site.AllowedOrigins, enabledModules));
})
.WithName("GetInternalTokenLookup");

// The Collector's projection is in-memory and rebuilds on restart (it owns no persistent data —
// docs/architecture/bounded-contexts-and-data-ownership.md) — this is its startup warm-up / periodic
// full-resync source. Same unauthenticated, network-boundary-trusting pattern as the endpoint above.
// Not paginated — the active-token count is small enough today that it doesn't need it; revisit if
// that stops being true.
app.MapGet("/internal/tokens", async (SiteRegistryDbContext db) =>
{
    var activeTokens = await db.SiteTokens
        .Where(t => t.RevokedAt == null)
        .Join(db.Sites, t => t.SiteId, s => s.Id, (t, s) => new { t.Token, s.Id, s.WorkspaceId, s.AllowedOrigins })
        .ToListAsync();

    var enabledModulesBySite = await db.SiteModuleSettings
        .Where(m => m.Enabled)
        .Select(m => new { m.SiteId, Module = m.Module.ToString() })
        .ToListAsync();

    var result = activeTokens
        .Select(t => new InternalTokenListEntry(
            t.Token,
            t.Id,
            t.WorkspaceId,
            t.AllowedOrigins,
            enabledModulesBySite.Where(m => m.SiteId == t.Id).Select(m => m.Module).ToArray()))
        .ToList();

    return Results.Ok(result);
})
.WithName("ListInternalTokens");

app.Run();

static async Task<string[]> GetEnabledModuleNamesAsync(SiteRegistryDbContext db, Guid siteId) =>
    (await db.SiteModuleSettings
        .Where(m => m.SiteId == siteId && m.Enabled)
        .Select(m => m.Module.ToString())
        .ToListAsync())
    .ToArray();

static string GenerateSiteToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    .TrimEnd('=')
    .Replace('+', '-')
    .Replace('/', '_');

// Ties an outbox row back to the HTTP request that wrote it (distributed tracing, M00.5) — ASP.NET
// Core starts an Activity per request whenever a listener is subscribed, which
// packages/dotnet-service-defaults's AddAspNetCoreInstrumentation() always is, so Activity.Current is
// reliably set for every endpoint here. Falls back to a fresh id only for the unusual case of no
// active Activity (e.g. called outside a request).
static string CurrentCorrelationId() => Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

// The event catalogue defines only one key-related event type (site.key.rotated.v1) — both issuing
// and revoking a token use it, with Action distinguishing which happened (see
// services/site-registry/README.md).
static void AddSiteKeyRotatedOutboxEvent(SiteRegistryDbContext db, Site site, SiteToken token, string action)
{
    var eventData = new SiteKeyRotatedEventData(site.Id, token.Id, token.Token, action);
    db.OutboxEvents.Add(new OutboxEvent
    {
        Id = Guid.NewGuid(),
        EventType = EventTypes.SiteKeyRotatedV1,
        TenantId = site.WorkspaceId,
        SiteId = site.Id,
        CorrelationId = CurrentCorrelationId(),
        DataJson = JsonSerializer.Serialize(eventData),
        CreatedAt = DateTimeOffset.UtcNow,
    });
}

internal sealed record CreateSiteRequest(
    [property: Required] Guid WorkspaceId,
    [property: Required, MinLength(1), MaxLength(200)] string Name,
    [property: Required, MinLength(1), MaxLength(253)] string CanonicalDomain,
    [property: Required, MinLength(1)] string[] AllowedOrigins,
    [property: Required, MinLength(1), MaxLength(50)] string Environment);

internal sealed record CreateSiteResponse(
    Guid Id, Guid WorkspaceId, string Name, string CanonicalDomain, string[] AllowedOrigins,
    string Environment, DateTimeOffset CreatedAt, string InitialToken);

internal sealed record SiteSummaryDto(Guid Id, string Name, string CanonicalDomain, string Environment, DateTimeOffset CreatedAt);

internal sealed record SiteDetailDto(Guid Id, Guid WorkspaceId, string Name, string CanonicalDomain, string Environment, DateTimeOffset CreatedAt, Role Role);

internal sealed record SiteTokenDto(Guid Id, string Token, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

internal sealed record SiteModuleSettingDto(Module Module, bool Enabled, DateTimeOffset UpdatedAt);

internal sealed record UpdateModuleSettingRequest(bool Enabled);

/// <summary>Response shape for GET /internal/tokens/{token} — see the endpoint's doc comment.</summary>
internal sealed record InternalTokenLookupResponse(
    bool Found, Guid? SiteId, Guid? WorkspaceId, string[]? AllowedOrigins, string[]? EnabledModules)
{
    public static readonly InternalTokenLookupResponse NotFound = new(false, null, null, null, null);
}

/// <summary>One row of GET /internal/tokens — see the endpoint's doc comment.</summary>
internal sealed record InternalTokenListEntry(
    string Token, Guid SiteId, Guid WorkspaceId, string[] AllowedOrigins, string[] EnabledModules);

/// <summary>Event-specific payload carried in site.created.v1's EventEnvelope.Data (ADR 0002).</summary>
internal sealed record SiteCreatedEventData(
    Guid SiteId,
    Guid WorkspaceId,
    string Name,
    string CanonicalDomain,
    string[] AllowedOrigins,
    string Environment);

/// <summary>
/// Event-specific payload carried in site.key.rotated.v1's EventEnvelope.Data (ADR 0002). Action is
/// "issued" or "revoked" — the catalogue defines one event type for both key-set transitions.
/// </summary>
internal sealed record SiteKeyRotatedEventData(Guid SiteId, Guid TokenId, string Token, string Action);

/// <summary>Event-specific payload carried in site.settings.changed.v1's EventEnvelope.Data (ADR 0002).</summary>
internal sealed record SiteSettingsChangedEventData(Guid SiteId, Module Module, bool Enabled);

public partial class Program;
