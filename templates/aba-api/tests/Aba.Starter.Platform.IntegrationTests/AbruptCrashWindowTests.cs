using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Aba.Starter.Platform;
using Aba.Starter.Platform.Messaging;
using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;
using Wolverine.RabbitMQ;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>
/// Genuine child-process termination: kills the nested dotnet test host rather
/// than throwing an exception or gracefully stopping a WebApplication.
/// Explicit CI-only gate, exact disposable loopback DB, per-case GUID marker.
/// </summary>
public sealed class AbruptCrashWindowTests
{
    private const string ChildMode = "ABA_CI_CRASH_CHILD_MODE";
    private const string ChildProvider = "ABA_CI_CRASH_PROVIDER";
    private const string ChildId = "ABA_CI_CRASH_ID";
    private const string ChildMarker = "ABA_CI_CRASH_MARKER";
    private const string ChildQueue = "ABA_CI_CRASH_RABBIT_QUEUE";
    private const string ParentPipe = "ABA_CI_CRASH_PARENT_PIPE";

    private static string ConfiguredConnection(string provider) =>
        Environment.GetEnvironmentVariable(provider == "PostgreSql"
            ? "ABA_TEST_POSTGRESQL" : "ABA_TEST_SQLSERVER")
        ?? throw new InvalidOperationException("Missing disposable CI database connection.");

    private static void RequireIsolatedCi(string provider, string connection,
        bool ci, bool explicitlyEnabled)
    {
        if (!ci || !explicitlyEnabled)
            throw new InvalidOperationException("Abrupt crash drills require CI and explicit opt-in.");
        if (provider == "PostgreSql")
        {
            var info = new NpgsqlConnectionStringBuilder(connection);
            if (info.Host is not ("localhost" or "127.0.0.1") ||
                info.Port != 5432 || info.Database != "aba_ci" ||
                info.Username != "aba_ci")
                throw new InvalidOperationException(
                    "PostgreSQL crash drill requires the exact loopback aba_ci fixture.");
        }
        else if (provider == "SqlServer")
        {
            var info = new SqlConnectionStringBuilder(connection);
            if (info.DataSource is not ("localhost,1433" or "127.0.0.1,1433") ||
                info.InitialCatalog != "aba_ci" || info.UserID != "sa")
                throw new InvalidOperationException(
                    "SQL Server crash drill requires the exact loopback aba_ci fixture.");
        }
        else
        {
            throw new InvalidOperationException("Unknown crash drill database provider.");
        }
    }

    internal static void RequireCurrentFixture(string provider, string connection) =>
        RequireIsolatedCi(provider, connection,
            Environment.GetEnvironmentVariable("CI") == "true",
            Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes");

    internal static void RequireBrokerCrashDrill(bool ci, bool destructiveDrill, bool brokerOutageDrill)
    {
        if (!ci || !destructiveDrill || !brokerOutageDrill)
            throw new InvalidOperationException(
                "The broker crash-window drill requires CI and both explicit destructive and broker-outage opt-ins.");
    }

    private static async Task<WebApplication> StartHostAsync(
        string provider, string connection, string? rabbitConnection = null,
        string? rabbitQueue = null)
    {
        if ((rabbitConnection is null) != (rabbitQueue is null))
            throw new InvalidOperationException("RabbitMQ connection and queue must be configured together.");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var configuration = new Dictionary<string, string?>
        {
            ["Platform:Database:Provider"] = provider,
            ["ConnectionStrings:AbaDatabase"] = connection,
            ["Platform:Cache:Provider"] = "Memory",
            ["Platform:Messaging:Enabled"] = "true",
            ["Platform:Messaging:AutoProvisionInDevelopment"] = "true"
        };
        if (rabbitConnection is not null)
        {
            configuration["Platform:Messaging:Transport"] = "RabbitMQ";
            configuration["ConnectionStrings:AbaRabbitMQ"] = rabbitConnection;
        }
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Services.AddHealthChecks();
        if (rabbitQueue is null)
        {
            builder.AddAbaPlatform();
        }
        else
        {
            builder.AddAbaPlatform(options =>
            {
                options.PublishMessage<AbaDurabilityProbe>()
                    .ToRabbitQueue(rabbitQueue)
                    .UseDurableOutbox();
                options.ListenToRabbitQueue(rabbitQueue).UseDurableInbox();
            });
        }
        var app = builder.Build();
        try
        {
            await using (var setup = app.Services.CreateAsyncScope())
            {
                var db = setup.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
                for (var i = 0; ; i++)
                {
                    try { await db.Database.MigrateAsync(); break; }
                    catch (Exception ex) when (i < 29 &&
                        (ex is System.Data.Common.DbException or TimeoutException))
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

    internal static async Task<int> ReceiptCountAsync(
        string provider, string connection, string scope, string? key)
    {
        var options = new DbContextOptionsBuilder<AbaPlatformDbContext>();
        if (provider == "PostgreSql")
            options.UseNpgsql(connection);
        else
            options.UseSqlServer(connection);
        await using var db = new AbaPlatformDbContext(options.Options);
        if (key is null)
            return await db.IdempotencyReceipts.AsNoTracking().CountAsync(x => x.Scope == scope);
        return await db.IdempotencyReceipts.AsNoTracking().CountAsync(x =>
            x.Scope == scope && x.Key == key);
    }

    /// <summary>
    /// Executed only by the disposable nested test runner. It parks after
    /// DB flush but before transaction commit, or after Wolverine has
    /// persisted a scheduled envelope, for the parent to SIGKILL.
    /// </summary>
    [Fact]
    [Trait("Category", "CrashDrill")]
    public async Task CrashChildWorker()
    {
        var mode = Environment.GetEnvironmentVariable(ChildMode);
        if (string.IsNullOrWhiteSpace(mode)) return;
        if (mode is not ("uncommitted" or "scheduled" or "preack" or "broker-outbox"))
            throw new InvalidOperationException("Unknown crash injection mode.");
        var provider = Environment.GetEnvironmentVariable(ChildProvider)
            ?? throw new InvalidOperationException("No child provider.");
        var connection = ConfiguredConnection(provider);
        RequireCurrentFixture(provider, connection);
        var id = Guid.Parse(Environment.GetEnvironmentVariable(ChildId)
            ?? throw new InvalidOperationException("No child probe ID."));
        var marker = Environment.GetEnvironmentVariable(ChildMarker)
            ?? throw new InvalidOperationException("No child acknowledgement marker.");
        AbaCrashAckProbeHandler.RequireIsolatedCrashMarker(marker,
            Environment.GetEnvironmentVariable("CI") == "true",
            Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes");

        if (mode == "broker-outbox")
        {
            if (provider != "PostgreSql")
                throw new InvalidOperationException("The broker crash-window probe uses the disposable PostgreSQL fixture.");
            RequireBrokerCrashDrill(
                Environment.GetEnvironmentVariable("CI") == "true",
                Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes",
                Environment.GetEnvironmentVariable("ABA_ENABLE_BROKER_OUTAGE_DRILL") == "yes");

            var rabbitConnection = Environment.GetEnvironmentVariable("ABA_TEST_RABBITMQ")
                ?? throw new InvalidOperationException("Missing disposable RabbitMQ connection.");
            var queue = Environment.GetEnvironmentVariable(ChildQueue)
                ?? throw new InvalidOperationException("No child RabbitMQ queue.");
            var pipeName = Environment.GetEnvironmentVariable(ParentPipe)
                ?? throw new InvalidOperationException("No parent synchronization pipe.");
            await using var brokerHost = await StartHostAsync(
                provider, connection, rabbitConnection, queue);

            // WebApplication.StartAsync is not a RabbitMQ readiness barrier.
            // Prove that this unique queue can publish and consume before
            // the parent pauses its disposable broker. Otherwise a pending
            // auto-provisioned topology can block the outbox invocation and
            // falsely report a missing SQL commit.
            var warmupId = Guid.NewGuid();
            await using (var warmupScope = brokerHost.Services.CreateAsyncScope())
            {
                var warmupBus = warmupScope.ServiceProvider.GetRequiredService<IMessageBus>();
                await warmupBus.PublishAsync(new AbaDurabilityProbe(warmupId));
            }

            var warmupDeadline = DateTimeOffset.UtcNow.AddSeconds(45);
            while (await ReceiptCountAsync(
                       provider, connection, "aba-durability-probe", warmupId.ToString("N")) != 1)
            {
                if (DateTimeOffset.UtcNow >= warmupDeadline)
                    throw new TimeoutException(
                        "RabbitMQ warmup did not commit its isolated technical receipt before broker pause.");
                await Task.Delay(250);
            }

            await using var pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(60_000);
            using var reader = new StreamReader(pipe, Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.ASCII,
                bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("host-ready");
            var release = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(2));
            if (release != "invoke-outbox")
                throw new InvalidOperationException("The parent did not release the broker crash probe.");

            await using (var scope = brokerHost.Services.CreateAsyncScope())
            {
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.InvokeAsync(new AbaTransactionalOutboxProbe(id, FailAfterPublish: false));
            }

            if (await ReceiptCountAsync(provider, connection,
                    "aba-outbox-transaction", id.ToString("N")) != 1 ||
                await ReceiptCountAsync(provider, connection,
                    "aba-durability-probe", id.ToString("N")) != 0)
                throw new InvalidOperationException(
                    "The broker-outage child did not reach the committed-outbox/no-effect boundary.");

            AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(marker,
                "outbox-committed-broker-paused",
                Environment.GetEnvironmentVariable("CI") == "true",
                Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes",
                atomic: true);
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return;
        }

        await using var host = await StartHostAsync(provider, connection);
        if (mode == "uncommitted")
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            db.IdempotencyReceipts.Add(new IdempotencyReceipt
            {
                Scope = "aba-sigkill-uncommitted",
                Key = id.ToString("N"),
                CreatedUtc = DateTimeOffset.UtcNow
            });
            // This was FLUSHED to the engine, but is still uncommitted.
            await db.SaveChangesAsync();
            AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(marker, "flushed-but-not-committed",
                Environment.GetEnvironmentVariable("CI") == "true",
                Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        else if (mode == "scheduled")
        {
            await using var scope = host.Services.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            // The message cannot execute until after its due time, even if
            // the process is SIGKILLed immediately after durable scheduling.
            await bus.ScheduleAsync(new AbaDurabilityProbe(id),
                TimeSpan.FromSeconds(12));
            AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(marker, "durable-schedule-returned",
                Environment.GetEnvironmentVariable("CI") == "true",
                Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        else
        {
            // This handler commits the test-only effect separately, then
            // signals before Wolverine is allowed to ACK the envelope.
            await using var scope = host.Services.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.SendAsync(new AbaCrashAckProbe(id));
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
    }

    private static string ProjectFile()
    {
        var parent = new DirectoryInfo(AppContext.BaseDirectory);
        while (parent is not null)
        {
            var project = parent.GetFiles("*.Platform.IntegrationTests.csproj");
            if (project.Length == 1)
                return project[0].FullName;
            parent = parent.Parent;
        }
        throw new InvalidOperationException("Cannot locate current generated integration project.");
    }

    internal static ProcessStartInfo CreateCrashWorkerStartInfo(
        string provider, string mode, Guid id, string marker,
        string? rabbitQueue = null, string? parentPipe = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false
        };
        start.ArgumentList.Add("test");
        start.ArgumentList.Add(ProjectFile());
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--no-restore");
        start.ArgumentList.Add("--filter");
        start.ArgumentList.Add("FullyQualifiedName~CrashChildWorker");
        start.ArgumentList.Add("--logger");
        start.ArgumentList.Add("console;verbosity=minimal");
        start.Environment[ChildMode] = mode;
        start.Environment[ChildProvider] = provider;
        start.Environment[ChildId] = id.ToString();
        start.Environment[ChildMarker] = marker;
        if (rabbitQueue is not null)
            start.Environment[ChildQueue] = rabbitQueue;
        if (parentPipe is not null)
            start.Environment[ParentPipe] = parentPipe;
        if (mode == "preack")
            start.Environment["ABA_CI_CRASH_ACK_MARKER"] = marker;
        return start;
    }

    private static async Task KillAfterMarkerAsync(string provider,
        string mode, Guid id, string marker)
    {
        var start = CreateCrashWorkerStartInfo(provider, mode, id, marker);
        using var child = Process.Start(start) ??
            throw new InvalidOperationException("Could not launch nested .NET crash worker.");
        try
        {
            var signalled = false;
            for (var i = 0; i < 180; i++)
            {
                if (File.Exists(marker)) { signalled = true; break; }
                if (child.HasExited)
                    throw new InvalidOperationException(
                        "Crash worker exited before its fault boundary; child code did not reach the marker.");
                await Task.Delay(250);
            }
            Assert.True(signalled, "Timed out before the real child reached the crash boundary.");
            if (mode == "preack")
                Assert.Equal("effect-committed-before-ack", File.ReadAllText(marker));
            // Actual SIGKILL on Linux, not StopAsync or an injected exception.
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotEqual(0, child.ExitCode);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
        }
    }

    [Theory]
    [Trait("Category", "CrashDrill")]
    [InlineData("PostgreSql", "uncommitted")]
    [InlineData("SqlServer", "uncommitted")]
    [InlineData("PostgreSql", "scheduled")]
    [InlineData("SqlServer", "scheduled")]
    [InlineData("PostgreSql", "preack")]
    [InlineData("SqlServer", "preack")]
    public async Task KilledProcessRollsBackRecoversAndDeduplicatesBeforeAck(
        string provider, string mode)
    {
        var connection = ConfiguredConnection(provider);
        RequireCurrentFixture(provider, connection);
        var id = Guid.NewGuid();
        var directory = Path.Combine(Path.GetTempPath(), "aba-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "child-ready.marker");
        try
        {
            await KillAfterMarkerAsync(provider, mode, id, marker);
            if (mode == "uncommitted")
            {
                Assert.Equal(0, await ReceiptCountAsync(provider, connection,
                    "aba-sigkill-uncommitted", id.ToString("N")));
                return;
            }

            await using var recovered = await StartHostAsync(provider, connection);
            try
            {
                bool delivered = false;
                for (var i = 0; i < (mode == "preack" ? 300 : 180); i++)
                {
                    if (mode == "preack")
                    {
                        // A fresh attempt proves the previously unacked
                        // Wolverine envelope was re-delivered after SIGKILL.
                        var attempts = await ReceiptCountAsync(provider, connection,
                            "aba-crash-attempt-" + id.ToString("N"), null);
                        if (attempts >= 2) { delivered = true; break; }
                    }
                    else if (await ReceiptCountAsync(provider, connection,
                        "aba-durability-probe", id.ToString("N")) == 1)
                    {
                        delivered = true;
                        break;
                    }
                    await Task.Delay(500);
                }
                Assert.True(delivered, mode == "preack"
                    ? provider + ": no redelivery occurred after ACK-window SIGKILL."
                    : provider + ": SIGKILL lost a persisted scheduled message.");
                Assert.Equal(1, await ReceiptCountAsync(provider, connection,
                    mode == "preack" ? "aba-crash-effect" : "aba-durability-probe",
                    id.ToString("N")));
            }
            finally
            {
                await recovered.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "CrashDrill")]
    public void CrashMarkerCannotEscapeDisposableDirectoryOrOverwriteExistingEvidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aba-crash-" + Guid.NewGuid().ToString("N"));
        var marker = Path.Combine(directory, "child-ready.marker");
        var sentinel = Path.Combine(Path.GetTempPath(), "aba-protected-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(sentinel, "untouched");
        try
        {
            // No IO without both gates; no arbitrary temp file or alternate filename.
            Assert.Throws<InvalidOperationException>(() =>
                AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(marker, "bad", false, true));
            Assert.Throws<InvalidOperationException>(() =>
                AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(marker, "bad", true, false));
            Assert.Throws<InvalidOperationException>(() =>
                AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(sentinel, "bad", true, true));
            Assert.Throws<InvalidOperationException>(() =>
                AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(
                    Path.Combine(directory, "other.marker"), "bad", true, true));
            Assert.False(File.Exists(marker));

            // Valid atomic signal is readable; repeating it must never replace evidence.
            AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(marker, "ready", true, true,
                atomic: true);
            Assert.Equal("ready", File.ReadAllText(marker));
            Assert.Throws<InvalidOperationException>(() =>
                AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(marker, "overwrite", true, true,
                    atomic: true));
            Assert.Equal("ready", File.ReadAllText(marker));
            File.Delete(marker);

            if (OperatingSystem.IsLinux())
            {
                // An attacker-controlled destination symlink must not redirect writes.
                File.CreateSymbolicLink(marker, sentinel);
                Assert.Throws<InvalidOperationException>(() =>
                    AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(marker, "overwrite", true, true));
                Assert.Equal("untouched", File.ReadAllText(sentinel));
                File.Delete(marker);

                // Exclusive staged creation blocks a pre-planted symlink as well.
                File.CreateSymbolicLink(marker + ".staged", sentinel);
                Assert.NotNull(Record.Exception(() =>
                    AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(marker, "overwrite",
                        true, true, atomic: true)));
                Assert.False(File.Exists(marker));
                Assert.Equal("untouched", File.ReadAllText(sentinel));

                // An alternate directory symlink with a valid-looking name is denied.
                var alias = Path.Combine(Path.GetTempPath(),
                    "aba-crash-" + Guid.NewGuid().ToString("N"));
                Directory.CreateSymbolicLink(alias, directory);
                try
                {
                    Assert.Throws<InvalidOperationException>(() =>
                        AbaCrashAckProbeHandler.WriteIsolatedCrashMarker(
                            Path.Combine(alias, "child-ready.marker"), "overwrite", true, true));
                    Assert.Equal("untouched", File.ReadAllText(sentinel));
                }
                finally
                {
                    Directory.Delete(alias);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            File.Delete(sentinel);
        }
    }

    [Fact]
    [Trait("Category", "CrashDrill")]
    public void RefusesProductionRemoteAndNonOptedInDatabaseKills()
    {
        const string pg = "Host=localhost;Port=5432;Database=aba_ci;Username=aba_ci;Password=fixture";
        const string sql = "Server=localhost,1433;Database=aba_ci;User Id=sa;Password=fixture;TrustServerCertificate=True";
        Assert.Throws<InvalidOperationException>(() =>
            RequireIsolatedCi("PostgreSql", pg, ci: false, explicitlyEnabled: true));
        Assert.Throws<InvalidOperationException>(() =>
            RequireIsolatedCi("PostgreSql", pg, ci: true, explicitlyEnabled: false));
        Assert.Throws<InvalidOperationException>(() =>
            RequireIsolatedCi("PostgreSql",
                pg.Replace("localhost", "prod.example.com"), true, true));
        Assert.Throws<InvalidOperationException>(() =>
            RequireIsolatedCi("PostgreSql",
                pg.Replace("Database=aba_ci", "Database=customer"), true, true));
        Assert.Throws<InvalidOperationException>(() =>
            RequireIsolatedCi("SqlServer",
                sql.Replace("localhost,1433", "prod.example.com,1433"), true, true));
        Assert.Throws<InvalidOperationException>(() =>
            RequireIsolatedCi("SqlServer",
                sql.Replace("Database=aba_ci", "Database=customer"), true, true));
        Assert.Throws<InvalidOperationException>(() =>
            RequireIsolatedCi("Unknown", pg, true, true));
    }

    [Theory]
    [Trait("Category", "BrokerCrashDrill")]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void BrokerProducerKillRequiresBothOptInsAndCi(
        bool ci, bool destructiveDrill, bool brokerOutageDrill)
    {
        Assert.Throws<InvalidOperationException>(() =>
            RequireBrokerCrashDrill(ci, destructiveDrill, brokerOutageDrill));
    }
}
