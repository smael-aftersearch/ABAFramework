using System.Threading.RateLimiting;
using Aba.Starter.Api.Platform;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var authority = builder.Configuration["Authentication:Authority"];
var audience = builder.Configuration["Authentication:Audience"];
StartupSecurityGuard.Validate(builder.Environment.IsDevelopment(), authority, audience);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // No keys or development-only signing secret are shipped with the template.
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = true;
    });
builder.Services.AddAuthorization(options =>
{
    // Newly added routes are protected unless they explicitly opt out.
    options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var requestedId = context.Request.Headers["X-Correlation-ID"].ToString();
    var valid = requestedId.Length is > 0 and <= 64
        && requestedId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
    context.Response.Headers["X-Correlation-ID"] = valid
        ? requestedId
        : context.TraceIdentifier;
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live").AllowAnonymous().WithTags("Health");
var statusRoute = app.MapGet("/api/v1/system/status", (IHostEnvironment env) =>
    Results.Ok(new SystemStatusV1("Aba.Starter", "v1", env.EnvironmentName)))
    .WithName("GetSystemStatus")
    .WithTags("System")
    .Produces<SystemStatusV1>(StatusCodes.Status200OK);

if (app.Environment.IsDevelopment())
{
    // Local-only developer conveniences: never publish these anonymously in production.
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.DisableAgent()).AllowAnonymous();
    app.MapGet("/", () => Results.Redirect("/scalar")).AllowAnonymous().ExcludeFromDescription();
    statusRoute.AllowAnonymous();
}

app.Run();

public sealed record SystemStatusV1(string Service, string ApiVersion, string Environment);
public partial class Program { }
