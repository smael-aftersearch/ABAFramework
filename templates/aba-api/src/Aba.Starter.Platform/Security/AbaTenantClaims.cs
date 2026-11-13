using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
    /// <summary>
    /// A composed principal is trusted only when exactly one identity was authenticated.
    /// Claims in unsigned/secondary identities must not become tenant authorization.
    /// Authentication middleware must already have validated this identity.
    /// </summary>
    public static ClaimsIdentity? TrustedIdentity(ClaimsPrincipal? principal)
    {
        ClaimsIdentity? trusted = null;
        if (principal is null)
            return null;
        foreach (var identity in principal.Identities)
        {
            if (!identity.IsAuthenticated)
                continue;
            if (trusted is not null)
                return null;
            trusted = identity;
        }
        return trusted;
    }

    /// <summary>Resolve only the single authenticated identity's claim, never a different identity's claim.</summary>
    public static string? Resolve(ClaimsPrincipal? principal, string claimType)
    {
        var identity = TrustedIdentity(principal);
        if (identity is null)
            return null;
        // A token may carry repeated claims of the same type. Selecting the
        // first would silently grant a tenant from an ambiguous identity.
        // Reject even repeated identical values: authorization needs one source.
        string? value = null;
        foreach (var claim in identity.FindAll(claimType))
        {
            if (value is not null)
                return null;
            value = claim.Value;
        }
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
            return null;
        return value;
    }

    public static AuthorizationPolicy BuildPolicy(AbaTenantOptions options, string authenticationScheme)
    {
        var builder = new AuthorizationPolicyBuilder(authenticationScheme)
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
