using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aba.Starter.Api.Platform;

/// <summary>
/// Browser origins allowed to call this host. An empty list disables browser
/// cross-origin access; CORS is not an authentication or authorization policy.
/// </summary>
public sealed record AbaCorsOptions(IReadOnlyList<string> AllowedOrigins)
{
    public const string PolicyName = "AbaBrowserClients";
    private const string ConfigurationPath = "Api:Cors:AllowedOrigins";

    public static AbaCorsOptions FromConfiguration(
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredOrigins = configuration.GetSection(ConfigurationPath).Get<string[]>() ?? [];
        var origins = new List<string>(configuredOrigins.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var origin in configuredOrigins)
        {
            if (!IsSafeOrigin(origin, isDevelopment))
                throw new InvalidOperationException(
                    $"{ConfigurationPath} must contain canonical HTTPS origins; HTTP loopback origins are allowed only in Development.");

            if (seen.Add(origin))
                origins.Add(origin);
        }

        return new AbaCorsOptions(origins);
    }

    private static bool IsSafeOrigin(string? origin, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(origin)
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || uri.AbsolutePath != "/"
            || !string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        return isDevelopment
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback;
    }
}

public static class AbaCorsServiceCollectionExtensions
{
    private static readonly string[] AllowedMethods = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"];
    private static readonly string[] AllowedHeaders =
        ["Authorization", "Content-Type", "X-Correlation-ID", "Idempotency-Key"];
    private static readonly string[] ExposedHeaders = ["X-Correlation-ID", "Retry-After"];

    public static IServiceCollection AddAbaCors(
        this IServiceCollection services,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var cors = AbaCorsOptions.FromConfiguration(configuration, isDevelopment);
            var policy = new CorsPolicyBuilder();
            if (cors.AllowedOrigins.Count == 0)
            {
                options.AddPolicy(AbaCorsOptions.PolicyName, policy.Build());
                return;
            }

            policy.WithOrigins(cors.AllowedOrigins.ToArray())
                .WithMethods(AllowedMethods)
                .WithHeaders(AllowedHeaders)
                .WithExposedHeaders(ExposedHeaders);
            // Credentials are deliberately not enabled. Browser clients must
            // use the host's bearer-token authentication policy.
            options.AddPolicy(AbaCorsOptions.PolicyName, policy.Build());
        });

        return services;
    }
}
