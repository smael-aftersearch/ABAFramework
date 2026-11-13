using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Aba.Starter.Platform.Caching;

/// <summary>Readiness check for the separate opt-in coordination connection.</summary>
public sealed class AbaCacheDistributedCoordinationHealthCheck(
    AbaCacheDistributedCoordinationOptions options,
    IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            return HealthCheckResult.Healthy();

        try
        {
            await services.GetRequiredService<IAbaCacheMissCoordinator>()
                .VerifyReadinessAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (AbaCacheCoordinationConfigurationMismatchException)
        {
            return HealthCheckResult.Unhealthy("Distributed cache coordination settings differ between replicas.");
        }
        catch (AbaCacheMissCoordinationUnavailableException)
        {
            return HealthCheckResult.Unhealthy("Distributed cache coordination Redis unavailable.");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Distributed cache coordination Redis unavailable.");
        }
    }
}
