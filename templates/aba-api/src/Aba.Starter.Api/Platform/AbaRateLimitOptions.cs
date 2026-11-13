using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Aba.Starter.Api.Platform;

/// <summary>
/// Per-instance ASP.NET rate limiter. Scopes authenticated requests by validated
/// JWT subject (and tenant if present), otherwise by TCP peer address only.
/// The caller's Host, X-Forwarded-For and X-Tenant-ID headers are not trusted.
/// </summary>
public sealed record AbaRateLimitOptions(int PermitLimit, int WindowSeconds)
{
    public static AbaRateLimitOptions FromConfiguration(IConfiguration config)
    {
        var permits = config.GetValue<int?>("Platform:RateLimit:PermitLimit") ?? 120;
        var window = config.GetValue<int?>("Platform:RateLimit:WindowSeconds") ?? 60;
        if (permits is < 1 or > 10000 || window is < 1 or > 3600)
            throw new InvalidOperationException(
                "Platform:RateLimit:PermitLimit must be 1..10000 and WindowSeconds 1..3600.");
        return new AbaRateLimitOptions(permits, window);
    }

    public static string PartitionKey(HttpContext context)
    {
        // Never compose an authenticated flag, subject or tenant across identities.
        // With ambiguous authentication, use the single connection-peer quota.
        var identity = Aba.Starter.Platform.Security.AbaTenantClaims.TrustedIdentity(context.User);
        if (identity is not null)
        {
            // Repeated/conflicting signed subject identifiers must not be
            // selected by first match to create additional quota buckets.
            string? subject = null;
            foreach (var claim in identity.Claims)
            {
                if (claim.Type != "sub" && claim.Type != ClaimTypes.NameIdentifier)
                    continue;
                if (subject is not null)
                    return PeerPartition(context);
                subject = claim.Value;
            }
            if (!string.IsNullOrWhiteSpace(subject))
            {
                var tenant = Aba.Starter.Platform.Security.AbaTenantClaims.Resolve(context.User, "tenant_id") ?? "";
                var bytes = Encoding.UTF8.GetBytes(
                    $"tenant={tenant.Length}:{tenant}|subject={subject.Length}:{subject}");
                return "jwt:" + Convert.ToHexString(SHA256.HashData(bytes)[..12]);
            }
        }

        // Only the actual connection peer is used. Reverse proxy trust must
        // be configured explicitly in a controlled gateway; never use headers.
        return PeerPartition(context);
    }

    private static string PeerPartition(HttpContext context)
    {
        // Dual-stack listeners can expose the SAME IPv4 TCP peer as either
        // 192.0.2.25 or ::ffff:192.0.2.25. Never grant two quota budgets.
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true)
            address = address.MapToIPv4();
        return "ip:" + (address?.ToString() ?? "unknown");
    }

    public static bool IsHealthPath(PathString path) =>
        path.Equals("/health/live", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase);

    public RateLimitPartition<string> ForRequest(HttpContext context)
    {
        if (IsHealthPath(context.Request.Path))
            return RateLimitPartition.GetNoLimiter("health");

        return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = PermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(WindowSeconds)
            });
    }
}
