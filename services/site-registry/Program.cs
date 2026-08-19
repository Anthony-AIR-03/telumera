using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

using Telumera.EventContracts;
using Telumera.ServiceDefaults;
using Telumera.Services.SiteRegistry.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();
builder.Services.AddOpenApi();

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
builder.Services.AddHostedService<OutboxPublisher>();
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
        BrowserToken = GenerateBrowserToken(),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    db.Sites.Add(site);

    var eventData = new SiteCreatedEventData(
        site.Id, site.WorkspaceId, site.Name, site.CanonicalDomain, site.AllowedOrigins, site.Environment);

    db.OutboxEvents.Add(new OutboxEvent
    {
        Id = Guid.NewGuid(),
        EventType = EventTypes.SiteCreatedV1,
        WorkspaceId = site.WorkspaceId,
        SiteId = site.Id,
        CorrelationId = Guid.NewGuid(),
        DataJson = JsonSerializer.Serialize(eventData),
        CreatedAt = site.CreatedAt,
    });

    // Single SaveChangesAsync call = single transaction: the Site row and its OutboxEvent row
    // commit together or not at all (docs/adr/0004-transactional-outbox-and-idempotent-consumers.md).
    await db.SaveChangesAsync();

    return Results.Created($"/sites/{site.Id}", site);
})
.WithName("CreateSite")
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

    return Results.Ok(site);
})
.WithName("GetSite")
.RequireAuthorization("ApiScope");

app.Run();

static string GenerateBrowserToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    .TrimEnd('=')
    .Replace('+', '-')
    .Replace('/', '_');

internal sealed record CreateSiteRequest(
    [property: Required] Guid WorkspaceId,
    [property: Required, MinLength(1), MaxLength(200)] string Name,
    [property: Required, MinLength(1), MaxLength(253)] string CanonicalDomain,
    [property: Required, MinLength(1)] string[] AllowedOrigins,
    [property: Required, MinLength(1), MaxLength(50)] string Environment);

/// <summary>Event-specific payload carried in site.created.v1's EventEnvelope.Data (ADR 0002).</summary>
internal sealed record SiteCreatedEventData(
    Guid SiteId,
    Guid WorkspaceId,
    string Name,
    string CanonicalDomain,
    string[] AllowedOrigins,
    string Environment);

public partial class Program;
