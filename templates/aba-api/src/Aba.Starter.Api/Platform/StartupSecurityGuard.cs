namespace Aba.Starter.Api.Platform;

/// <summary>Reject insecure production configuration before opening a port.</summary>
public static class StartupSecurityGuard
{
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
