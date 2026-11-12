using System.Diagnostics;
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

    private static void RequireCurrentFixture(string provider, string connection) =>
        RequireIsolatedCi(provider, connection,
            Environment.GetEnvironmentVariable("CI") == "true",
            Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes");

    private static async Task<WebApplication> StartHostAsync(
        string provider, string connection)
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

    private static async Task<int> ReceiptCountAsync(
        string provider, string connection, string scope, string key)
    {
        var options = new DbContextOptionsBuilder<AbaPlatformDbContext>();
        if (provider == "PostgreSql")
            options.UseNpgsql(connection);
        else
            options.UseSqlServer(connection);
        await using var db = new AbaPlatformDbContext(options.Options);
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
        if (mode is not ("uncommitted" or "scheduled"))
            throw new InvalidOperationException("Unknown crash injection mode.");
        var provider = Environment.GetEnvironmentVariable(ChildProvider)
            ?? throw new InvalidOperationException("No child provider.");
        var connection = ConfiguredConnection(provider);
        RequireCurrentFixture(provider, connection);
        var id = Guid.Parse(Environment.GetEnvironmentVariable(ChildId)
            ?? throw new InvalidOperationException("No child probe ID."));
        var marker = Environment.GetEnvironmentVariable(ChildMarker)
            ?? throw new InvalidOperationException("No child acknowledgement marker.");
        if (!Path.GetFullPath(marker).StartsWith(Path.GetFullPath(Path.GetTempPath()),
            StringComparison.Ordinal))
            throw new InvalidOperationException("Crash marker must be in the ephemeral temp directory.");

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
            File.WriteAllText(marker, "flushed-but-not-committed");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        else
        {
            await using var scope = host.Services.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            // The message cannot execute until after its due time, even if
            // the process is SIGKILLed immediately after durable scheduling.
            await bus.ScheduleAsync(new AbaDurabilityProbe(id),
                TimeSpan.FromSeconds(12));
            File.WriteAllText(marker, "durable-schedule-returned");
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

    private static async Task KillAfterMarkerAsync(string provider,
        string mode, Guid id, string marker)
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
    public async Task KilledProcessDoesNotCommitUnfinishedWorkOrLoseDurableSchedule(
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
                for (var i = 0; i < 180; i++)
                {
                    if (await ReceiptCountAsync(provider, connection,
                        "aba-durability-probe", id.ToString("N")) == 1)
                    {
                        delivered = true;
                        break;
                    }
                    await Task.Delay(500);
                }
                Assert.True(delivered,
                    provider + ": SIGKILL lost a persisted scheduled message.");
                Assert.Equal(1, await ReceiptCountAsync(provider, connection,
                    "aba-durability-probe", id.ToString("N")));
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
}
