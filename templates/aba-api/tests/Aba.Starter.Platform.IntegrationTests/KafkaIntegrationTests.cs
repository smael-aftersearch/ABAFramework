using Aba.Starter.Platform;
using Aba.Starter.Platform.Messaging;
using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Confluent.Kafka;
using Wolverine;
using Wolverine.Kafka;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

public sealed class KafkaIntegrationTests
{
    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    [Trait("Category", "BrokerIntegration")]
    public async Task KafkaDurableDeliveryHandlesDuplicatePayloadOnce(
        string provider, string databaseVariable)
    {
        var bootstrapServers = RequiredEnvironment("ABA_TEST_KAFKA");
        var databaseConnection = RequiredEnvironment(databaseVariable);
        var topic = "aba.technical.probe.v1." + Guid.NewGuid().ToString("N");
        var app = await StartMessagingHostAsync(provider, databaseConnection, bootstrapServers, topic);

        try
        {
            var messageId = Guid.NewGuid();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new AbaDurabilityProbe(messageId));
                await bus.PublishAsync(new AbaDurabilityProbe(messageId));
            }

            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            var deliveryAttempts = 0;
            var effects = 0;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
                var key = messageId.ToString("N");
                deliveryAttempts = await db.IdempotencyReceipts.AsNoTracking().CountAsync(x =>
                    x.Scope == "aba-durability-probe-attempt" && x.Key.StartsWith(key + ":"));
                effects = await db.IdempotencyReceipts.AsNoTracking().CountAsync(x =>
                    x.Scope == "aba-durability-probe" && x.Key == key);
                if (deliveryAttempts >= 2)
                    break;
                await Task.Delay(250);
            }

            Assert.True(deliveryAttempts >= 2,
                $"Kafka did not deliver both published envelopes to the durable inbox. " +
                $"Observed {deliveryAttempts} attempts and {effects} committed effects.");
            Assert.Equal(1, effects);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task<WebApplication> StartMessagingHostAsync(
        string provider, string databaseConnection, string bootstrapServers, string topic)
    {
        var groupId = "aba-integration-" + Guid.NewGuid().ToString("N");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Database:Provider"] = provider,
            ["ConnectionStrings:AbaDatabase"] = databaseConnection,
            ["Platform:Cache:Provider"] = "Memory",
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:Transport"] = "Kafka",
            ["Platform:Messaging:AutoProvisionInDevelopment"] = "true",
            ["ConnectionStrings:AbaKafka"] = bootstrapServers
        });
        builder.Services.AddHealthChecks();
        builder.AddAbaPlatform(options =>
        {
            options.Discovery.IncludeAssembly(typeof(AbaDurabilityProbeHandler).Assembly);
            options.PublishMessage<AbaDurabilityProbe>()
                .ToKafkaTopic(topic)
                .UseDurableOutbox();
            options.ListenToKafkaTopic(topic)
                .UseDurableInbox()
                .ConfigureConsumer(consumer =>
                {
                    consumer.GroupId = groupId;
                    consumer.AutoOffsetReset = AutoOffsetReset.Earliest;
                });
        });

        var app = builder.Build();
        await using (var bootstrap = app.Services.CreateAsyncScope())
        {
            var db = bootstrap.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            Exception? lastError = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    await db.Database.MigrateAsync();
                    lastError = null;
                    break;
                }
                catch (Exception ex) when (
                    ex is System.Data.Common.DbException || ex is TimeoutException)
                {
                    lastError = ex;
                    if (attempt < 29)
                        await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
            if (lastError is not null)
                throw new InvalidOperationException("The integration database migration did not complete.");
        }
        await app.StartAsync();
        return app;
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Missing required integration setting: {name}");
        return value!;
    }
}
