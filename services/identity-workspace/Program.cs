using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

using Telumera.ServiceDefaults;
using Telumera.Services.IdentityWorkspace.Api;

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

app.MapPost("/workspaces", async (CreateWorkspaceRequest request, IdentityWorkspaceDbContext db) =>
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
    await db.SaveChangesAsync();

    return Results.Created($"/workspaces/{workspace.Id}", workspace);
})
.WithName("CreateWorkspace")
.RequireAuthorization("ApiScope");

app.MapGet("/workspaces/{id:guid}", async (Guid id, IdentityWorkspaceDbContext db) =>
{
    var workspace = await db.Workspaces.FindAsync(id);
    return workspace is null ? Results.NotFound() : Results.Ok(workspace);
})
.WithName("GetWorkspace")
.RequireAuthorization("ApiScope");

app.Run();

internal sealed record CreateWorkspaceRequest([property: Required, MinLength(1), MaxLength(200)] string Name);

public partial class Program;
