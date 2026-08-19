using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

using Telumera.Gateway;
using Telumera.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();
builder.Services.AddOpenApi();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Same policy as identity-workspace/site-registry — requires the access_as_user delegated scope
// rather than a fallback policy, so /health/live and /health/ready stay open for container
// healthchecks.
builder.Services.AddAuthorization(options => options.AddPolicy("ApiScope", policy =>
    policy.RequireClaim(ClaimConstants.Scope, "access_as_user")));

builder.Services.AddHttpClient();
builder.Services.AddSingleton<GatewayForwarder>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// See GatewayForwarder.cs for the routing/auth-forwarding design and gateway/README.md for why
// this is a transparent pass-through rather than hand-written per-endpoint routes.
app.Map("/{**path}", (HttpContext httpContext, GatewayForwarder forwarder) => forwarder.ForwardAsync(httpContext))
    .RequireAuthorization("ApiScope");

app.Run();

public partial class Program;
