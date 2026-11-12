using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aba.Starter.Platform.Security;

/// <summary>
/// Technical tenant-claim enforcement only. This does not decide tenant membership,
/// which belongs to the actual owning Identity business context/provider.
/// </summary>
public sealed record AbaTenantOptions(bool Enabled, string ClaimType)
{
    public static AbaTenantOptions FromConfiguration(IConfiguration config)
    {
        var enabled = config.GetValue<bool>("Platform:Tenancy:Enabled");
        var claimType = config["Platform:Tenancy:ClaimType"] ?? "tenant_id";
        if (string.IsNullOrWhiteSpace(claimType) || claimType.Length > 128)
            throw new InvalidOperationException("Platform:Tenancy:ClaimType must be a nonempty claim name.");
        return new(enabled, claimType);
    }
}

public interface IRequestTenant
{
    /// <summary>Validated tenant claim or null; never trusts an unvalidated request header.</summary>
    string? TenantId { get; }
}

public sealed class AuthenticatedTenantResolver(
    Microsoft.AspNetCore.Http.IHttpContextAccessor accessor,
    AbaTenantOptions options) : IRequestTenant
{
    public string? TenantId =>
        AbaTenantClaims.Resolve(accessor.HttpContext?.User, options.ClaimType);
}

public static class AbaTenantClaims
{
    /// <summary>Only invoke after the caller's authentication middleware has validated the principal.</summary>
    public static string? Resolve(ClaimsPrincipal? principal, string claimType)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;
        var value = principal.FindFirst(claimType)?.Value;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
            return null;
        return value;
    }

    public static AuthorizationPolicy BuildPolicy(AbaTenantOptions options)
    {
        var builder = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser();
        if (options.Enabled)
            builder.RequireAssertion(ctx => Resolve(ctx.User, options.ClaimType) is not null);
        return builder.Build();
    }

    public static IServiceCollection AddAbaTenantResolution(
        this IServiceCollection services, AbaTenantOptions options)
    {
        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.AddScoped<IRequestTenant, AuthenticatedTenantResolver>();
        return services;
    }
}
