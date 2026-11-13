using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Aba.Starter.Platform;
using Aba.Starter.Platform.Messaging;
using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.RabbitMQ;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

public sealed class RabbitMqIntegrationTests
{
    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    [Trait("Category", "BrokerIntegration")]
    public async Task RabbitMqDurableDeliveryHandlesDuplicatePayloadOnce(
        string provider, string databaseVariable)
    {
        var rabbitConnection = RequiredEnvironment("ABA_TEST_RABBITMQ");
        var databaseConnection = RequiredEnvironment(databaseVariable);
        var queue = "aba.technical.probe.v1." + Guid.NewGuid().ToString("N");
        var app = await StartMessagingHostAsync(provider, databaseConnection, rabbitConnection, queue);

        try
        {
            var messageId = Guid.NewGuid();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new AbaDurabilityProbe(messageId));
                await bus.PublishAsync(new AbaDurabilityProbe(messageId));
            }

            var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
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
                $"RabbitMQ did not deliver both published envelopes to the durable inbox. " +
                $"Observed {deliveryAttempts} attempts and {effects} committed effects.");
            Assert.Equal(1, effects);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "BrokerOutageDrill")]
    public async Task DurableOutboxDeliversAfterTheDisposableRabbitMqBrokerRecovers()
    {
        Assert.Equal("true", Environment.GetEnvironmentVariable("CI"));
        Assert.Equal("yes", Environment.GetEnvironmentVariable("ABA_ENABLE_BROKER_OUTAGE_DRILL"));
        var containerId = RequiredEnvironment("ABA_TEST_RABBITMQ_CONTAINER_ID");
        Assert.Matches(new Regex("^[a-fA-F0-9]{12,64}$", RegexOptions.CultureInvariant), containerId);

        var rabbitConnection = RequiredEnvironment("ABA_TEST_RABBITMQ");
        var endpoint = new Uri(rabbitConnection);
        Assert.True(endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || endpoint.Host == "127.0.0.1" || endpoint.Host == "::1",
            "The outage drill only accepts a loopback RabbitMQ endpoint.");
        var databaseConnection = RequiredEnvironment("ABA_TEST_POSTGRESQL");
        var queue = "aba.technical.outage.v1." + Guid.NewGuid().ToString("N");
        var app = await StartMessagingHostAsync(
            "PostgreSql", databaseConnection, rabbitConnection, queue);

        var paused = false;
        try
        {
            await VerifyDisposableRabbitContainerAsync(containerId);
            await RunDockerAsync("pause", containerId);
            paused = true;

            var messageId = Guid.NewGuid();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.InvokeAsync(new AbaTransactionalOutboxProbe(messageId, FailAfterPublish: false));
            }

            await WaitForReceiptAsync(app.Services, "aba-outbox-transaction", messageId,
                TimeSpan.FromSeconds(15));
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.Equal(0, await ReadReceiptCountAsync(
                app.Services, "aba-durability-probe", messageId));

            await RunDockerAsync("unpause", containerId);
            paused = false;
            await WaitForReceiptAsync(app.Services, "aba-durability-probe", messageId,
                TimeSpan.FromSeconds(60));
            Assert.Equal(1, await ReadReceiptCountAsync(
                app.Services, "aba-durability-probe", messageId));
        }
        finally
        {
            if (paused)
                await RunDockerAsync("unpause", containerId);
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task<WebApplication> StartMessagingHostAsync(
        string provider, string databaseConnection, string rabbitConnection, string queue)
    {
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
            ["Platform:Messaging:Transport"] = "RabbitMQ",
            ["Platform:Messaging:AutoProvisionInDevelopment"] = "true",
            ["ConnectionStrings:AbaRabbitMQ"] = rabbitConnection
        });
        builder.Services.AddHealthChecks();
        builder.AddAbaPlatform(options =>
        {
            options.Discovery.IncludeAssembly(typeof(AbaDurabilityProbeHandler).Assembly);
            options.PublishMessage<AbaDurabilityProbe>()
                .ToRabbitQueue(queue)
                .UseDurableOutbox();
            options.ListenToRabbitQueue(queue).UseDurableInbox();
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

    private static async Task WaitForReceiptAsync(
        IServiceProvider services, string scope, Guid messageId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scopeService = services.CreateAsyncScope();
            var db = scopeService.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            if (await db.IdempotencyReceipts.AsNoTracking().AnyAsync(x =>
                x.Scope == scope && x.Key == messageId.ToString("N")))
                return;
            await Task.Delay(250);
        }
        throw new TimeoutException($"No technical receipt appeared for scope '{scope}'.");
    }

    private static async Task<int> ReadReceiptCountAsync(
        IServiceProvider services, string scope, Guid messageId)
    {
        await using var verification = services.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        return await db.IdempotencyReceipts.AsNoTracking().CountAsync(x =>
            x.Scope == scope && x.Key == messageId.ToString("N"));
    }

    private static async Task VerifyDisposableRabbitContainerAsync(string id)
    {
        var details = await RunDockerAsync("inspect", "--format",
            "{{.Id}}|{{.Config.Image}}|{{.State.Status}}|{{index .Config.Labels \"aba.test.disposable\"}}", id);
        var parts = details.Trim().Split('|');
        Assert.Equal(4, parts.Length);
        Assert.StartsWith(id, parts[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("rabbitmq:4.4.0-management-alpine", parts[1]);
        Assert.Equal("running", parts[2]);
        Assert.Equal("true", parts[3]);
    }

    private static async Task<string> RunDockerAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the Docker CLI for the isolated broker drill.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Docker operation '{arguments[0]}' failed (exit {process.ExitCode}): {error.Trim()}");
        return output;
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Missing required integration setting: {name}");
        return value!;
    }
}
