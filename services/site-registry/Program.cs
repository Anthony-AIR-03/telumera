using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Telumera.EventContracts;
using Telumera.ServiceDefaults;
using Telumera.Services.SiteRegistry.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("SiteRegistry")
    ?? throw new InvalidOperationException("ConnectionStrings:SiteRegistry is not configured.");

builder.Services.AddDbContext<SiteRegistryDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddHealthChecks().AddNpgSql(connectionString, tags: ["ready"]);
builder.Services.AddHttpClient();
builder.Services.AddHostedService<OutboxPublisher>();

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

// No auth is enforced on these endpoints yet — Entra ID integration is deferred to a later
// M00.4 task, and WorkspaceId below is trusted as given rather than validated against
// identity-workspace (see the doc comment on Site.WorkspaceId). Known, explicitly temporary gaps.
app.MapPost("/sites", async (CreateSiteRequest request, SiteRegistryDbContext db) =>
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
.WithName("CreateSite");

app.MapGet("/sites/{id:guid}", async (Guid id, SiteRegistryDbContext db) =>
{
    var site = await db.Sites.FindAsync(id);
    return site is null ? Results.NotFound() : Results.Ok(site);
})
.WithName("GetSite");

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
