using System.ComponentModel.DataAnnotations;
using Telumera.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Example endpoint demonstrating the validation + ProblemDetails convention this template provides.
// Delete this once the service defines its real endpoints.
app.MapPost("/example", (ExampleRequest request) =>
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

    return Results.Ok(new { request.Name, Received = DateTimeOffset.UtcNow });
})
.WithName("PostExample");

app.Run();

internal sealed record ExampleRequest([property: Required, MinLength(1)] string Name);
