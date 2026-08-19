using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

using Telumera.ServiceDefaults;
using Telumera.Services.IdentityWorkspace.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();
builder.Services.AddOpenApi();

// Lets Role be passed/returned as "Viewer"/"Developer"/etc. instead of its numeric value.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Requires the access_as_user delegated scope (docs/adr — Telumera API app registration's "Expose an
// API" page) rather than a fallback policy, so /health/live and /health/ready (MapDefaultEndpoints,
// packages/dotnet-service-defaults) stay open for container healthchecks.
builder.Services.AddAuthorization(options => options.AddPolicy("ApiScope", policy =>
    policy.RequireClaim(ClaimConstants.Scope, "access_as_user")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserAccessor>();

var connectionString = builder.Configuration.GetConnectionString("IdentityWorkspace")
    ?? throw new InvalidOperationException("ConnectionStrings:IdentityWorkspace is not configured.");

builder.Services.AddDbContext<IdentityWorkspaceDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddHealthChecks().AddNpgSql(connectionString, tags: ["ready"]);

var app = builder.Build();

// No CI/migration-bundle pipeline exists yet (M00.5) — auto-migrate at startup so
// `docker compose up` stays self-sufficient for local development.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>().Database.Migrate();
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/workspaces", async (CreateWorkspaceRequest request, IdentityWorkspaceDbContext db, CurrentUserAccessor currentUser) =>
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

    var workspace = new Workspace
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        CreatedAt = DateTimeOffset.UtcNow,
    };
    db.Workspaces.Add(workspace);

    // A workspace with no members would be unmanageable by anyone — the creator is always its Owner.
    var creator = await currentUser.GetOrProvisionAsync();
    db.Memberships.Add(new Membership
    {
        Id = Guid.NewGuid(),
        UserId = creator.Id,
        WorkspaceId = workspace.Id,
        Role = Role.Owner,
        CreatedAt = DateTimeOffset.UtcNow,
    });

    await db.SaveChangesAsync();

    return Results.Created($"/workspaces/{workspace.Id}", workspace);
})
.WithName("CreateWorkspace")
.RequireAuthorization("ApiScope");

app.MapGet("/workspaces/{id:guid}", async (Guid id, IdentityWorkspaceDbContext db, CurrentUserAccessor currentUser) =>
{
    var workspace = await db.Workspaces.FindAsync(id);
    if (workspace is null)
    {
        return Results.NotFound();
    }

    var caller = await currentUser.GetOrProvisionAsync();
    var role = await GetRoleAsync(db, id, caller.Id);
    if (role is null)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    return Results.Ok(workspace);
})
.WithName("GetWorkspace")
.RequireAuthorization("ApiScope");

// Requires Admin+ in the target workspace. JIT-creates a stub User for the target Entra Object ID if
// they've never authenticated yet — their profile fields (DisplayName/Email) fill in whenever they
// first do, via CurrentUserAccessor.
app.MapPost("/workspaces/{id:guid}/members", async (Guid id, AddMemberRequest request, IdentityWorkspaceDbContext db, CurrentUserAccessor currentUser) =>
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

    var workspace = await db.Workspaces.FindAsync(id);
    if (workspace is null)
    {
        return Results.NotFound();
    }

    var caller = await currentUser.GetOrProvisionAsync();
    var callerRole = await GetRoleAsync(db, id, caller.Id);
    if (callerRole is null || callerRole < Role.Admin)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var targetUser = await db.Users.FirstOrDefaultAsync(u => u.EntraObjectId == request.EntraObjectId);
    if (targetUser is null)
    {
        targetUser = new User { Id = Guid.NewGuid(), EntraObjectId = request.EntraObjectId, CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(targetUser);
    }
    else if (await db.Memberships.AnyAsync(m => m.WorkspaceId == id && m.UserId == targetUser.Id))
    {
        return Results.Conflict("User is already a member of this workspace.");
    }

    var membership = new Membership
    {
        Id = Guid.NewGuid(),
        UserId = targetUser.Id,
        WorkspaceId = id,
        Role = request.Role,
        CreatedAt = DateTimeOffset.UtcNow,
    };
    db.Memberships.Add(membership);
    await db.SaveChangesAsync();

    return Results.Created($"/workspaces/{id}/members/{membership.Id}", membership);
})
.WithName("AddWorkspaceMember")
.RequireAuthorization("ApiScope");

app.MapGet("/workspaces/{id:guid}/members", async (Guid id, IdentityWorkspaceDbContext db, CurrentUserAccessor currentUser) =>
{
    var workspace = await db.Workspaces.FindAsync(id);
    if (workspace is null)
    {
        return Results.NotFound();
    }

    var caller = await currentUser.GetOrProvisionAsync();
    var callerRole = await GetRoleAsync(db, id, caller.Id);
    if (callerRole is null)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var members = await db.Memberships
        .Where(m => m.WorkspaceId == id)
        .Join(db.Users, m => m.UserId, u => u.Id,
            (m, u) => new MemberDto(u.EntraObjectId, u.DisplayName, u.Email, m.Role))
        .ToListAsync();

    return Results.Ok(members);
})
.WithName("GetWorkspaceMembers")
.RequireAuthorization("ApiScope");

// Deliberately unauthenticated at the HTTP level — meant to be reached only via Dapr service
// invocation from within the compose network (see site-registry's OutboxPublisher-style HttpClient
// usage), not the service's public host port. Known, explicitly documented simplification: no
// service-to-service auth scheme exists yet, so this trusts the network boundary instead.
//
// Always returns 200 with a nullable Role, never 404 — "not a member" is a normal, expected
// outcome here, not an error condition. Returning 404 made Dapr's resiliency retry/circuit-breaker
// policy (infrastructure/dapr/components/resiliency.yaml) treat every "not a member" check as a
// failure worth retrying, which is wrong and tripped the circuit breaker on legitimate traffic.
app.MapGet("/internal/workspaces/{workspaceId:guid}/members/{entraObjectId}", async (Guid workspaceId, string entraObjectId, IdentityWorkspaceDbContext db) =>
{
    var role = await db.Memberships
        .Where(m => m.WorkspaceId == workspaceId)
        .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.EntraObjectId, m.Role })
        .Where(x => x.EntraObjectId == entraObjectId)
        .Select(x => (Role?)x.Role)
        .FirstOrDefaultAsync();

    return Results.Ok(new InternalMembershipResponse(role));
})
.WithName("GetInternalMembership");

app.Run();

static async Task<Role?> GetRoleAsync(IdentityWorkspaceDbContext db, Guid workspaceId, Guid userId)
{
    var membership = await db.Memberships
        .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId);
    return membership?.Role;
}

internal sealed record CreateWorkspaceRequest([property: Required, MinLength(1), MaxLength(200)] string Name);

internal sealed record AddMemberRequest([property: Required, MinLength(1)] string EntraObjectId, Role Role);

internal sealed record MemberDto(string EntraObjectId, string? DisplayName, string? Email, Role Role);

internal sealed record InternalMembershipResponse(Role? Role);

public partial class Program;
