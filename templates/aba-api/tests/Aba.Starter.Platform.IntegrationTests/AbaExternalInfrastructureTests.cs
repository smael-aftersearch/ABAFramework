using Aba.Starter.Platform;
using Aba.Starter.Platform.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Aba.Starter.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>Real service tests; CI must inject connections to run these cases.
/// External suite runs separately and FAILS when required connections are absent.</summary>
public sealed class AbaExternalInfrastructureTests
{
    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task BothRealDatabasesApplyMigrationsAndEnforceUniqueClaims(
        string provider, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        Assert.False(string.IsNullOrWhiteSpace(connection), $"Missing required integration connection: {variable}");
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
        Assert.False(string.IsNullOrWhiteSpace(connection), $"Missing required integration connection: {variable}");

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

    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task EfStateAndWolverineOutboxCommitOrRollbackTogether(
        string provider, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        Assert.False(string.IsNullOrWhiteSpace(connection),
            $"Missing required integration connection: {variable}");

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

        await using (var setupScope = app.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            // Needed for SQL Server: create application DB before Wolverine opens it.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await db.Database.MigrateAsync();
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
            var committedId = Guid.NewGuid();
            await using (var sendScope = app.Services.CreateAsyncScope())
            {
                var bus = sendScope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
                await bus.InvokeAsync(new Aba.Starter.Platform.Messaging.AbaTransactionalOutboxProbe(
                    committedId, FailAfterPublish: false));
            }

            var bothCommitted = false;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                await using var readScope = app.Services.CreateAsyncScope();
                var db = readScope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
                var key = committedId.ToString("N");
                var commandCount = await db.IdempotencyReceipts.CountAsync(x =>
                    x.Scope == "aba-outbox-transaction" && x.Key == key);
                var eventCount = await db.IdempotencyReceipts.CountAsync(x =>
                    x.Scope == "aba-durability-probe" && x.Key == key);
                if (commandCount == 1 && eventCount == 1)
                {
                    bothCommitted = true;
                    break;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
            Assert.True(bothCommitted,
                $"{provider}: committed EF state and Wolverine outbox event did not both appear.");

            var rejectedId = Guid.NewGuid();
            await using (var sendScope = app.Services.CreateAsyncScope())
            {
                var bus = sendScope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
                await Assert.ThrowsAnyAsync<Exception>(() =>
                    bus.InvokeAsync(new Aba.Starter.Platform.Messaging.AbaTransactionalOutboxProbe(
                        rejectedId, FailAfterPublish: true)));
            }

            // A faulty outbox must never dispatch the event while rolling back
            // the EF record. This checks both independently.
            await Task.Delay(TimeSpan.FromMilliseconds(750));
            await using var finalScope = app.Services.CreateAsyncScope();
            var finalDb = finalScope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            var badKey = rejectedId.ToString("N");
            Assert.False(await finalDb.IdempotencyReceipts.AsNoTracking().AnyAsync(x =>
                x.Scope == "aba-outbox-transaction" && x.Key == badKey));
            Assert.False(await finalDb.IdempotencyReceipts.AsNoTracking().AnyAsync(x =>
                x.Scope == "aba-durability-probe" && x.Key == badKey));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task IdempotencyReceiptRollsBackWithSameDbContextWork(
        string provider, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        Assert.False(string.IsNullOrWhiteSpace(connection),
            $"Missing required integration connection: {variable}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Database:Provider"] = provider,
                ["ConnectionStrings:AbaDatabase"] = connection
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaPersistence(AbaDatabaseOptions.FromConfiguration(config));
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        await db.Database.MigrateAsync();
        var store = scope.ServiceProvider.GetRequiredService<EfIdempotencyStore>();

        var failed = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ExecuteOnceInTransactionAsync("tx", failed, (ctx, _) =>
            {
                ctx.IdempotencyReceipts.Add(new IdempotencyReceipt
                {
                    Scope = "side-effect", Key = failed,
                    CreatedUtc = DateTimeOffset.UtcNow
                });
                throw new InvalidOperationException("Injected failure before EF commit.");
            }));
        Assert.False(await db.IdempotencyReceipts.AsNoTracking()
            .AnyAsync(x => x.Key == failed));

        var success = Guid.NewGuid().ToString("N");
        Assert.True(await store.ExecuteOnceInTransactionAsync("tx", success, (ctx, _) =>
        {
            ctx.IdempotencyReceipts.Add(new IdempotencyReceipt
            {
                Scope = "side-effect", Key = success,
                CreatedUtc = DateTimeOffset.UtcNow
            });
            return Task.CompletedTask;
        }));
        Assert.Equal(2, await db.IdempotencyReceipts.AsNoTracking()
            .CountAsync(x => x.Key == success));
        Assert.False(await store.ExecuteOnceInTransactionAsync(
            "tx", success, (_, _) => throw new InvalidOperationException(
                "Duplicate callback must not execute.")));
    }

    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task ConcurrentSameKeyClaimsHaveOneWinnerAndNoDuplicateWork(
        string provider, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        Assert.False(string.IsNullOrWhiteSpace(connection),
            $"Missing integration connection: {variable}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Database:Provider"] = provider,
                ["ConnectionStrings:AbaDatabase"] = connection
            }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAbaPersistence(AbaDatabaseOptions.FromConfiguration(config));
        await using var providerServices = services.BuildServiceProvider();
        await using (var bootstrap = providerServices.CreateAsyncScope())
        {
            var bootstrapDb = bootstrap.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            await bootstrapDb.Database.MigrateAsync();
        }
        var unique = Guid.NewGuid().ToString("N");
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = providerServices.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<EfIdempotencyStore>();
            await start.Task;
            return await store.ExecuteOnceInTransactionAsync("concurrent", unique,
                (db, ct) =>
                {
                    db.IdempotencyReceipts.Add(new IdempotencyReceipt
                    {
                        Scope = "concurrent-work",
                        Key = unique,
                        CreatedUtc = DateTimeOffset.UtcNow
                    });
                    return Task.CompletedTask;
                });
        }).ToArray();

        start.SetResult(true);
        var outcomes = await Task.WhenAll(tasks);
        Assert.Equal(1, outcomes.Count(success => success));
        Assert.Equal(3, outcomes.Count(success => !success));
        await using var verification = providerServices.CreateAsyncScope();
        var dbVerify = verification.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        Assert.Equal(1, await dbVerify.IdempotencyReceipts.CountAsync(x =>
            x.Scope == "concurrent" && x.Key == unique));
        Assert.Equal(1, await dbVerify.IdempotencyReceipts.CountAsync(x =>
            x.Scope == "concurrent-work" && x.Key == unique));
    }

    [Fact]
    public async Task ActualRedisClientCanBypassUnreachableServerForAuthorizedQueriesOnly()
    {
        // Reserve an unused loopback TCP port, then release it. The real Redis
        // client will attempt a network connection to that deliberately offline port.
        var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Cache:Provider"] = "Redis",
                ["ConnectionStrings:AbaRedis"] =
                    $"127.0.0.1:{port},abortConnect=false,connectRetry=0,connectTimeout=400,asyncTimeout=400"
            }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAbaCaching(settings);
        await using var provider = services.BuildServiceProvider();
        var query = provider.GetRequiredService<ReadThroughQueryCache>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var sourceCalls = 0;

        var value = await query.GetOrLoadAsync(
            "authorized-tenant", "noncritical-query", TimeSpan.FromMinutes(1),
            _ =>
            {
                sourceCalls++;
                return Task.FromResult("fresh-source-value");
            },
            CacheOutagePolicy.UseAuthoritativeSource, deadline.Token);
        Assert.Equal("fresh-source-value", value);
        Assert.Equal(1, sourceCalls);

        var strictFailure = await Record.ExceptionAsync(() => query.GetOrLoadAsync(
            "authorized-tenant", "strict-query", TimeSpan.FromMinutes(1),
            _ =>
            {
                sourceCalls++;
                return Task.FromResult("must-not-load");
            },
            CacheOutagePolicy.FailClosed, deadline.Token));
        Assert.NotNull(strictFailure);
        Assert.True(strictFailure is StackExchange.Redis.RedisException or TimeoutException,
            $"Unexpected failure type: {strictFailure.GetType().FullName}");
        Assert.Equal(1, sourceCalls);
    }

    [Fact]
    public async Task RealRedisProvidesScopedRoundTripAndInvalidation()
    {
        var connection = Environment.GetEnvironmentVariable("ABA_TEST_REDIS");
        Assert.False(string.IsNullOrWhiteSpace(connection), "Missing required integration connection: ABA_TEST_REDIS");
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
