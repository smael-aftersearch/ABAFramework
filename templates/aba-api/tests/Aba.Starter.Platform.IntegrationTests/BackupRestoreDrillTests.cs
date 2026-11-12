using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>
/// Destructive database-native drills ONLY for disposable CI databases. Never
/// point these tests at real business tables, production databases or replicas.
/// The database names and server connections are independently safety-checked.
/// </summary>
public sealed class BackupRestoreDrillTests
{
    private static void RequireDisposableFixture(string provider, string connection, string? flag, string? ci)
    {
        if (flag != "yes" || !string.Equals(ci, "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recovery drills require an explicit CI-only opt-in.");

        if (provider == "PostgreSql")
        {
            var settings = new NpgsqlConnectionStringBuilder(connection);
            if (settings.Host is not ("localhost" or "127.0.0.1")
                || settings.Port != 5432 || settings.Database != "aba_ci"
                || settings.Username != "aba_ci")
                throw new InvalidOperationException("Recovery drills require disposable local PostgreSQL CI fixture.");
        }
        else if (provider == "SqlServer")
        {
            var settings = new SqlConnectionStringBuilder(connection);
            if (settings.DataSource is not ("localhost,1433" or "127.0.0.1,1433")
                || settings.InitialCatalog != "aba_ci" || settings.UserID != "sa")
                throw new InvalidOperationException("Recovery drills require disposable local SQL Server CI fixture.");
        }
        else
            throw new InvalidOperationException("Unsupported recovery drill provider.");
    }

    [Theory]
    [Trait("Category", "RecoveryDrill")]
    [InlineData("PostgreSql", "Host=prod.example;Port=5432;Database=aba_ci;Username=aba_ci;Password=fake")]
    [InlineData("PostgreSql", "Host=localhost;Port=5432;Database=real_customer;Username=aba_ci;Password=fake")]
    [InlineData("SqlServer", "Server=prod.example,1433;Database=aba_ci;User ID=sa;Password=fake")]
    [InlineData("SqlServer", "Server=localhost,1433;Database=real_customer;User ID=sa;Password=fake")]
    public void RefusesNonIsolatedDatabases(string provider, string unsafeConnection)
    {
        Assert.Throws<InvalidOperationException>(() =>
            RequireDisposableFixture(provider, unsafeConnection, "yes", "true"));
    }

    [Fact]
    [Trait("Category", "RecoveryDrill")]
    public void RefusesImplicitOrNonCiExecution()
    {
        const string safe = "Host=localhost;Port=5432;Database=aba_ci;Username=aba_ci;Password=fake";
        Assert.Throws<InvalidOperationException>(() =>
            RequireDisposableFixture("PostgreSql", safe, null, "true"));
        Assert.Throws<InvalidOperationException>(() =>
            RequireDisposableFixture("PostgreSql", safe, "yes", null));
    }

    [Theory]
    [Trait("Category", "RecoveryDrill")]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task ProviderNativeBackupRestoresOriginalDataAfterMutation(
        string provider, string configurationVariable)
    {
        var connection = Environment.GetEnvironmentVariable(configurationVariable)
            ?? throw new InvalidOperationException($"Missing {configurationVariable}.");
        RequireDisposableFixture(provider, connection,
            Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS"),
            Environment.GetEnvironmentVariable("CI"));

        if (provider == "PostgreSql")
            await VerifyPostgreSqlAsync(connection);
        else
            await VerifySqlServerAsync(connection);
    }

    private static async Task VerifyPostgreSqlAsync(string connection)
    {
        var settings = new NpgsqlConnectionStringBuilder(connection);
        var admin = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" };
        var suffix = Guid.NewGuid().ToString("N");
        var original = "aba_ci_recovery_" + suffix;
        var restored = "aba_ci_restored_" + suffix;
        var archive = Path.Combine(Path.GetTempPath(), "aba-ci-" + suffix + ".dump");
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();

        try
        {
            await PgSqlAsync(control, $"CREATE DATABASE \"{original}\"");
            var first = new NpgsqlConnectionStringBuilder(connection) { Database = original };
            await using (var source = new NpgsqlConnection(first.ConnectionString))
            {
                await source.OpenAsync();
                await PgSqlAsync(source, "CREATE TABLE aba_ci_restore_marker (value int NOT NULL)");
                await PgSqlAsync(source, "INSERT INTO aba_ci_restore_marker(value) VALUES (42)");
            }

            await PgProcessAsync("pg_dump", settings, original, archive, restore: false);
            await using (var mutated = new NpgsqlConnection(first.ConnectionString))
            {
                await mutated.OpenAsync();
                await PgSqlAsync(mutated, "UPDATE aba_ci_restore_marker SET value = 99");
                Assert.Equal(99, await PgValueAsync(mutated));
            }

            await PgSqlAsync(control, $"CREATE DATABASE \"{restored}\"");
            await PgProcessAsync("pg_restore", settings, restored, archive, restore: true);
            var second = new NpgsqlConnectionStringBuilder(connection) { Database = restored };
            await using var recovered = new NpgsqlConnection(second.ConnectionString);
            await recovered.OpenAsync();
            Assert.Equal(42, await PgValueAsync(recovered));
        }
        finally
        {
            // Only ever drop names generated under the guard above.
            // PostgreSQL 16 forcibly disconnects only our disposable fixture.
            await PgSqlAsync(control, $"DROP DATABASE IF EXISTS \"{restored}\" WITH (FORCE)");
            await PgSqlAsync(control, $"DROP DATABASE IF EXISTS \"{original}\" WITH (FORCE)");
            if (File.Exists(archive))
                File.Delete(archive);
        }
    }

    private static async Task PgSqlAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> PgValueAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT value FROM aba_ci_restore_marker", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task PgProcessAsync(
        string executable, NpgsqlConnectionStringBuilder settings,
        string database, string archive, bool restore)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--host");
        start.ArgumentList.Add(settings.Host);
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(settings.Port.ToString());
        start.ArgumentList.Add("--username");
        start.ArgumentList.Add(settings.Username);
        start.ArgumentList.Add("--dbname");
        start.ArgumentList.Add(database);
        if (restore)
        {
            start.ArgumentList.Add("--no-owner");
            start.ArgumentList.Add("--no-acl");
            start.ArgumentList.Add("--exit-on-error");
            start.ArgumentList.Add(archive);
        }
        else
        {
            start.ArgumentList.Add("--format=custom");
            start.ArgumentList.Add("--file");
            start.ArgumentList.Add(archive);
        }
        start.Environment["PGPASSWORD"] = settings.Password;
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Cannot start {executable}.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Recovery tool {executable} timed out.");
        }
        var stderr = await process.StandardError.ReadToEndAsync();
        Assert.True(process.ExitCode == 0,
            $"{executable} failed (exit {process.ExitCode}): {stderr[..Math.Min(500, stderr.Length)]}");
    }

    private static async Task VerifySqlServerAsync(string connection)
    {
        var settings = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        var name = "aba_ci_recovery_" + Guid.NewGuid().ToString("N");
        // This path exists inside the disposable Linux SQL Server service
        // container and is never published as an application endpoint.
        var backup = $"/var/opt/mssql/data/{name}.bak";
        await using var master = new SqlConnection(settings.ConnectionString);
        await master.OpenAsync();
        try
        {
            await MsSqlAsync(master, $"CREATE DATABASE [{name}]");
            await MsSqlAsync(master,
                $"CREATE TABLE [{name}].[dbo].[aba_ci_restore_marker] ([Value] int NOT NULL)");
            await MsSqlAsync(master,
                $"INSERT INTO [{name}].[dbo].[aba_ci_restore_marker] ([Value]) VALUES (42)");
            await MsSqlAsync(master,
                $"BACKUP DATABASE [{name}] TO DISK = N'{backup}' WITH INIT, CHECKSUM", 180);
            await MsSqlAsync(master,
                $"RESTORE VERIFYONLY FROM DISK = N'{backup}' WITH CHECKSUM", 180);
            await MsSqlAsync(master,
                $"UPDATE [{name}].[dbo].[aba_ci_restore_marker] SET [Value] = 99");
            Assert.Equal(99, await MsValueAsync(master, name));
            await MsSqlAsync(master,
                $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await MsSqlAsync(master,
                $"RESTORE DATABASE [{name}] FROM DISK = N'{backup}' WITH REPLACE, RECOVERY", 180);
            await MsSqlAsync(master, $"ALTER DATABASE [{name}] SET MULTI_USER");
            Assert.Equal(42, await MsValueAsync(master, name));
        }
        finally
        {
            // The backup file remains only in the ephemeral CI SQL Server
            // container; the disposable database is always removed.
            await MsSqlAsync(master,
                $"IF DB_ID(N'{name}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{name}]; END");
        }
    }

    private static async Task MsSqlAsync(SqlConnection connection, string query, int timeout = 60)
    {
        using var command = new SqlCommand(query, connection) { CommandTimeout = timeout };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> MsValueAsync(SqlConnection connection, string database)
    {
        using var command = new SqlCommand(
            $"SELECT [Value] FROM [{database}].[dbo].[aba_ci_restore_marker]", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
