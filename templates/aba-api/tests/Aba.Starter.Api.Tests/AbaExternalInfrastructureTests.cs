using Aba.Starter.Platform;
using Aba.Starter.Platform.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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


    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task WolverineDurableLocalMessageProcessesOnActualDatabase(
        string provider, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(connection)) return;

        // The test uses an isolated ephemeral HTTP host and explicitly opt-in
        // development-only schema provisioning for Wolverine's durable tables.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Database:Provider"] = provider,
            ["ConnectionStrings:AbaDatabase"] = connection,
            ["Platform:Cache:Provider"] = "Memory",
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:AutoProvisionInDevelopment"] = "true"
        });
        builder.Services.AddHealthChecks();
        builder.AddAbaPlatform();
        await using var app = builder.Build();
        // EF owns the idempotency table and creates the DB itself. Wolverine
        // cannot provision its own relational message schema until the target
        // database exists (especially important for SQL Server).
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var context = setup.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await context.Database.MigrateAsync();
                    break;
                }
                catch (Exception ex) when (attempt < 29 &&
                    (ex is System.Data.Common.DbException || ex is TimeoutException))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }
        await app.StartAsync();
        try
        {
            var probeId = Guid.NewGuid();
            await using (var sendScope = app.Services.CreateAsyncScope())
            {
                var bus = sendScope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
                await bus.SendAsync(new Aba.Starter.Platform.Messaging.AbaDurabilityProbe(probeId));
            }
            var found = false;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
                if (await db.IdempotencyReceipts.AsNoTracking().AnyAsync(x =>
                    x.Scope == "aba-durability-probe" && x.Key == probeId.ToString("N")))
                {
                    found = true;
                    break;
                }
                await Task.Delay(250);
            }
            Assert.True(found, $"Wolverine did not durably deliver probe {probeId} with {provider}.");
        }
        finally
        {
            await app.StopAsync();
        }
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
