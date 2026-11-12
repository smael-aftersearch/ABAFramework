namespace Aba.Starter.Api.Platform;

/// <summary>Reject insecure production configuration before opening a port.</summary>
public static class StartupSecurityGuard
{
    /// <summary>
    /// HTTP OIDC discovery is permitted ONLY with explicit opt-in from a
    /// loopback identity provider in Development. Remote insecure discovery is
    /// rejected even if a developer accidentally enables the test setting.
    /// </summary>
    public static bool RequireHttpsMetadata(
        bool isDevelopment, bool allowLoopbackHttp, string? authority)
    {
        return !(isDevelopment && allowLoopbackHttp
            && Uri.TryCreate(authority, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback
            && string.IsNullOrWhiteSpace(uri.UserInfo));
    }

    public static void Validate(bool isDevelopment, string? authority, string? audience)
    {
        if (isDevelopment)
            return;

        if (string.IsNullOrWhiteSpace(audience)
            || !Uri.TryCreate(authority, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                "Non-development API requires Authentication:Authority (HTTPS OIDC URL) " +
                "and Authentication:Audience. Supply configuration securely before startup.");
        }
    }
}
