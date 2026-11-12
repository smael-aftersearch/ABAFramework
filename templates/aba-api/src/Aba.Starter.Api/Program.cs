using System.Threading.RateLimiting;
using Aba.Starter.Api.Platform;
using Aba.Starter.Platform;
using Aba.Starter.Platform.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
// Data, cache and Wolverine are opt-in; a blank project never needs an external service.
builder.AddAbaPlatform();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // No keys or development-only signing secret are shipped with the template.
        options.Authority = authority;
        options.Audience = audience;
        // HTTP discovery must be an explicit Development-only opt-in for
        // local identity-provider smoke tests. Production always requires HTTPS.
        options.RequireHttpsMetadata = StartupSecurityGuard.RequireHttpsMetadata(
            builder.Environment.IsDevelopment(),
            builder.Configuration.GetValue<bool>("Authentication:AllowHttpMetadataInDevelopment"),
            authority);
    });
// A tenant is trusted only when taken from a validated JWT claim. Anonymous
// infrastructure routes remain explicitly marked AllowAnonymous.
var tenancy = AbaTenantOptions.FromConfiguration(builder.Configuration);
builder.Services.AddAbaTenantResolution(tenancy);
builder.Services.AddAuthorization(options =>
{
    var baseline = AbaTenantClaims.BuildPolicy(tenancy, JwtBearerDefaults.AuthenticationScheme);
    options.DefaultPolicy = baseline;
    options.FallbackPolicy = baseline;
});
var rateLimit = AbaRateLimitOptions.FromConfiguration(builder.Configuration);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        rateLimit.ForRequest);
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
// Evaluate validated JWT identity before assigning a user's rate-limit bucket.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live").AllowAnonymous().WithTags("Health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous().WithTags("Health");
var statusRoute = app.MapGet("/api/v1/system/status", (IHostEnvironment env) =>
    Results.Ok(new SystemStatusV1("Aba.Starter", "v1", env.EnvironmentName)))
    .WithName("GetSystemStatus")
    .WithTags("System")
    .Produces<SystemStatusV1>(StatusCodes.Status200OK);

// A deliberately protected, domain-neutral probe. Useful for verifying that a
// configured JWT/OIDC authority is actually enforced in all environments.
app.MapGet("/api/v1/system/authorization-check", () => Results.NoContent())
    .WithName("CheckJwtAuthorization")
    .WithTags("System")
    .RequireAuthorization()
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status401Unauthorized);

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
