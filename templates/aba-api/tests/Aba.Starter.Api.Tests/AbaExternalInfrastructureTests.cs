using Aba.Starter.Platform.Caching;
using Aba.Starter.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Api.Tests;

/// <summary>Real service tests; CI must inject connections to run these cases.
/// Local `dotnet test` without containers remains a fast offline smoke run.</summary>
public sealed class AbaExternalInfrastructureTests
{
    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task BothRealDatabasesApplyMigrationsAndEnforceUniqueClaims(
        string provider, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(connection)) return; // Opt-in integration suite.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Database:Provider"] = provider,
                ["ConnectionStrings:AbaDatabase"] = connection
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaPersistence(AbaDatabaseOptions.FromConfiguration(configuration));
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        // SQL Server containers need time to accept logins after the TCP port opens.
        // Retrying a real EF migration does not mark a failing migration successful.
        Exception? lastError = null;
        var migrated = false;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                migrated = true;
                break;
            }
            catch (Exception ex) when (attempt < 29 &&
                (ex is System.Data.Common.DbException || ex is TimeoutException))
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
        Assert.True(migrated, lastError?.ToString());
        Assert.True(await db.Database.CanConnectAsync());
        var store = scope.ServiceProvider.GetRequiredService<EfIdempotencyStore>();
        var id = Guid.NewGuid().ToString("N");
        Assert.True(await store.TryClaimAsync("ci", id));
        Assert.False(await store.TryClaimAsync("ci", id));
        Assert.True(await store.TryClaimAsync("other-scope", id));
    }

    [Fact]
    public async Task RealRedisProvidesScopedRoundTripAndInvalidation()
    {
        var connection = Environment.GetEnvironmentVariable("ABA_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Cache:Provider"] = "Redis",
                ["ConnectionStrings:AbaRedis"] = connection
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaCaching(config);
        await using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<ScopedJsonCache>();
        var key = Guid.NewGuid().ToString("N");
        await cache.PutAsync("integration-a", key, "connected", TimeSpan.FromMinutes(1));
        Assert.Equal("connected", await cache.GetAsync<string>("integration-a", key));
        Assert.Null(await cache.GetAsync<string>("integration-b", key));
        await cache.RemoveAsync("integration-a", key);
        Assert.Null(await cache.GetAsync<string>("integration-a", key));
    }
}
