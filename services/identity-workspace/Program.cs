using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Telumera.ServiceDefaults;
using Telumera.Services.IdentityWorkspace.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();
builder.Services.AddOpenApi();

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

// No auth is enforced on these endpoints yet — Entra ID integration is deferred to a later
// M00.4 task (docs/architecture/bounded-contexts-and-data-ownership.md's Identity & Workspace
// row). This is a known, explicitly temporary gap, not an oversight.
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
.WithName("CreateWorkspace");

app.MapGet("/workspaces/{id:guid}", async (Guid id, IdentityWorkspaceDbContext db) =>
{
    var workspace = await db.Workspaces.FindAsync(id);
    return workspace is null ? Results.NotFound() : Results.Ok(workspace);
})
.WithName("GetWorkspace");

app.Run();

internal sealed record CreateWorkspaceRequest([property: Required, MinLength(1), MaxLength(200)] string Name);

public partial class Program;
