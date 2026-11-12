using Aba.Starter.Platform;
using Aba.Starter.Platform.Messaging;
using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>
/// A real persisted schedule must survive stopping the HTTP host and starting
/// another process against the same PostgreSQL or SQL Server database.
/// </summary>
public sealed class DurabilityRecoveryTests
{
    private static async Task<WebApplication> StartDurableHostAsync(string provider, string connection)
    {
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
        var app = builder.Build();

        try
        {
            await using (var setup = app.Services.CreateAsyncScope())
            {
                var db = setup.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
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
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task ScheduledDurableMessageIsRecoveredAfterHostRestart(
        string provider, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        Assert.False(string.IsNullOrWhiteSpace(connection),
            $"Missing required integration connection: {variable}");
        var messageId = Guid.NewGuid();

        await using (var first = await StartDurableHostAsync(provider, connection!))
        {
            await using var scope = first.Services.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            // The message is persisted in Wolverine's relational schedule.
            await bus.ScheduleAsync(new AbaDurabilityProbe(messageId),
                TimeSpan.FromSeconds(8));

            var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            Assert.False(await db.IdempotencyReceipts.AsNoTracking().AnyAsync(x =>
                x.Scope == "aba-durability-probe" && x.Key == messageId.ToString("N")));
            await first.StopAsync();
        }

        await using (var second = await StartDurableHostAsync(provider, connection!))
        {
            try
            {
                var processed = false;
                for (var i = 0; i < 90; i++)
                {
                    await using var scope = second.Services.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
                    if (await db.IdempotencyReceipts.AsNoTracking().AnyAsync(x =>
                        x.Scope == "aba-durability-probe" && x.Key == messageId.ToString("N")))
                    {
                        processed = true;
                        break;
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }

                Assert.True(processed,
                    $"{provider}: persisted scheduled message was lost across host restart.");
            }
            finally
            {
                await second.StopAsync();
            }
        }
    }
}
