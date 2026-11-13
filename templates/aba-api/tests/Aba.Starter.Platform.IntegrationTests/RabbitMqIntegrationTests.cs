using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
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
                // Keep the idempotency key shared while making both payloads distinct.
                await bus.PublishAsync(new AbaDurabilityProbe(messageId, Guid.NewGuid()));
                await bus.PublishAsync(new AbaDurabilityProbe(messageId, Guid.NewGuid()));
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

    [Fact]
    [Trait("Category", "BrokerCrashDrill")]
    public async Task KilledProducerRecoversCommittedOutboxAfterBrokerReturns()
    {
        AbruptCrashWindowTests.RequireBrokerCrashDrill(
            Environment.GetEnvironmentVariable("CI") == "true",
            Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes",
            Environment.GetEnvironmentVariable("ABA_ENABLE_BROKER_OUTAGE_DRILL") == "yes");

        var containerId = RequiredEnvironment("ABA_TEST_RABBITMQ_CONTAINER_ID");
        Assert.Matches(new Regex("^[a-fA-F0-9]{12,64}$", RegexOptions.CultureInvariant), containerId);
        var rabbitConnection = RequiredEnvironment("ABA_TEST_RABBITMQ");
        var endpoint = new Uri(rabbitConnection);
        Assert.True(endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || endpoint.Host == "127.0.0.1" || endpoint.Host == "::1",
            "The crash-window drill only accepts a loopback RabbitMQ endpoint.");
        var databaseConnection = RequiredEnvironment("ABA_TEST_POSTGRESQL");
        AbruptCrashWindowTests.RequireCurrentFixture("PostgreSql", databaseConnection);
        await VerifyDisposableRabbitContainerAsync(containerId);

        var id = Guid.NewGuid();
        var queue = "aba.technical.crash-outbox.v1." + Guid.NewGuid().ToString("N");
        var pipeName = "aba-crash-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "aba-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "child-ready.marker");
        Process? child = null;
        var brokerPaused = false;
        try
        {
            AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(marker, ci: true, explicitlyEnabled: true);
            await using var pipe = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var childProcess = Process.Start(AbruptCrashWindowTests.CreateCrashWorkerStartInfo(
                "PostgreSql", "broker-outbox", id, marker, queue, pipeName))
                ?? throw new InvalidOperationException("Could not launch the broker crash-window worker.");
            child = childProcess;

            await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromMinutes(2));
            using var reader = new StreamReader(pipe, Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.ASCII,
                bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            Assert.Equal("host-ready", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(1)));

            await RunDockerAsync("pause", containerId);
            brokerPaused = true;
            await writer.WriteLineAsync("invoke-outbox");

            // The child may block while the broker is down. Drain a fixed
            // vocabulary of non-secret phase signals from the existing private
            // pipe so a timeout points to the exact unfinished stage instead
            // of disguising all failures as a missing committed-outbox marker.
            var phases = new ConcurrentQueue<string>();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await reader.ReadLineAsync() is { } phase)
                    {
                        if (phase is "outbox-started" or "outbox-resolved"
                            or "outbox-published" or "outbox-flush-started"
                            or "outbox-committed" or "outbox-verified")
                            phases.Enqueue(phase);
                    }
                }
                catch (IOException) { /* The isolated child was killed or closed the pipe. */ }
                catch (ObjectDisposedException) { /* Parent cleanup closed the pipe. */ }
            });

            var signalled = false;
            for (var i = 0; i < 240; i++)
            {
                if (File.Exists(marker)) { signalled = true; break; }
                if (childProcess.HasExited)
                    throw new InvalidOperationException(
                        "The broker crash worker exited before the committed-outbox marker. " +
                        "Non-secret child phases: " + string.Join(" -> ", phases));
                await Task.Delay(250);
            }
            Assert.True(signalled,
                "Timed out before the producer committed its outbox command. " +
                "Non-secret child phases: " + string.Join(" -> ", phases));
            Assert.Equal("outbox-committed-broker-paused", File.ReadAllText(marker));

            childProcess.Kill(entireProcessTree: true);
            await childProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotEqual(0, childProcess.ExitCode);
            Assert.Equal(1, await AbruptCrashWindowTests.ReceiptCountAsync(
                "PostgreSql", databaseConnection, "aba-outbox-transaction", id.ToString("N")));
            Assert.Equal(0, await AbruptCrashWindowTests.ReceiptCountAsync(
                "PostgreSql", databaseConnection, "aba-durability-probe", id.ToString("N")));

            await RunDockerAsync("unpause", containerId);
            brokerPaused = false;

            await using var recovered = await StartMessagingHostAsync(
                "PostgreSql", databaseConnection, rabbitConnection, queue);
            await WaitForReceiptAsync(recovered.Services, "aba-durability-probe", id,
                TimeSpan.FromSeconds(90));
            await using (var scope = recovered.Services.CreateAsyncScope())
            {
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new AbaDurabilityProbe(id, Guid.NewGuid()));
            }

            var duplicateDelivered = false;
            for (var i = 0; i < 180; i++)
            {
                if (await ReadAttemptReceiptCountAsync(recovered.Services, id) >= 2)
                {
                    duplicateDelivered = true;
                    break;
                }
                await Task.Delay(250);
            }
            Assert.True(duplicateDelivered,
                "The intentional duplicate technical envelope did not reach the recovered host.");
            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.Equal(1, await ReadReceiptCountAsync(
                recovered.Services, "aba-durability-probe", id));
            Assert.True(await ReadAttemptReceiptCountAsync(recovered.Services, id) >= 2,
                "Expected the recovered envelope and the deliberate duplicate attempt.");
        }
        finally
        {
            try
            {
                if (brokerPaused)
                    await RunDockerAsync("unpause", containerId);
            }
            finally
            {
                try
                {
                    if (child is not null && !child.HasExited)
                    {
                        child.Kill(entireProcessTree: true);
                        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    }
                }
                finally
                {
                    child?.Dispose();
                    Directory.Delete(directory, recursive: true);
                }
            }
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

    private static async Task<int> ReadAttemptReceiptCountAsync(
        IServiceProvider services, Guid messageId)
    {
        await using var verification = services.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        var prefix = messageId.ToString("N") + ":";
        return await db.IdempotencyReceipts.AsNoTracking().CountAsync(x =>
            x.Scope == "aba-durability-probe-attempt" && x.Key.StartsWith(prefix));
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
