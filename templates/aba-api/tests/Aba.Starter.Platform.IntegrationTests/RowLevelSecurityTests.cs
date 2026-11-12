using System.Data.Common;
using Aba.Starter.Platform.Persistence;
using Aba.Starter.Platform.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>
/// Real provider RLS and EF ExecuteUpdate/raw-SQL bypass tests on a dedicated
/// disposable infrastructure table. No business schema or production access.
/// </summary>
public sealed class RowLevelSecurityTests
{
    private sealed record Tenant(string? TenantId) : IRequestTenant;

    private sealed class Row
    {
        public Guid Id { get; set; }
        public string TenantId { get; set; } = "";
        public string Note { get; set; } = "";
    }

    private sealed class RowDb(DbContextOptions<RowDb> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Row>(e =>
            {
                e.ToTable("aba_ci_rls_rows");
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId).HasMaxLength(128);
            });
        }
    }

    private const string TableName = "aba_ci_rls_rows";
    private static readonly Guid AlphaId = new("cee4e7e9-53ec-4cab-9c2a-3b0cc47bfdef");
    private static readonly Guid BetaId = new("75c6fe2e-115f-46ae-ab79-f5b7e6498cec");

    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task RlsDeniesForeignRowsForRawSqlAndEfBulkMutations(string engine, string variable)
    {
        var settings = Environment.GetEnvironmentVariable(variable);
        Assert.False(string.IsNullOrWhiteSpace(settings), $"Missing external service: {variable}");

        if (engine == "PostgreSql")
            await PostgreSqlRealPolicyAsync(settings!);
        else
            await SqlServerRealPolicyAsync(settings!);
    }

    private static async Task PostgreSqlRealPolicyAsync(string settings)
    {
        // The postgres service's bootstrap user is a superuser and bypasses RLS.
        // Authenticate a separate, least-privilege CI-only role to prove enforcement.
        var role = "aba_ci_rls_" + Guid.NewGuid().ToString("N")[..16];
        var pw = "AbaCiOnly_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(settings);
        await admin.OpenAsync();
        try
        {
            await ExecAsync(admin, "DROP TABLE IF EXISTS \"public\".\"aba_ci_rls_rows\" CASCADE");
            await ExecAsync(admin,
                "CREATE TABLE \"public\".\"aba_ci_rls_rows\" (" +
                "\"Id\" uuid NOT NULL PRIMARY KEY, \"TenantId\" varchar(128) NOT NULL, \"Note\" text NOT NULL)");
            await SeedAsync(admin, "PostgreSql");
            await ExecAsync(admin, $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{pw}'");
            await ExecAsync(admin, $"GRANT USAGE ON SCHEMA public TO \"{role}\"");
            await ExecAsync(admin,
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON \"public\".\"aba_ci_rls_rows\" TO \"{role}\"");
            foreach (var statement in AbaTenantRlsPolicies.Install("PostgreSql", "public", TableName))
                await ExecAsync(admin, statement);

            var restricted = new NpgsqlConnectionStringBuilder(settings)
            {
                Username = role, Password = pw, Pooling = false
            };
            await using var client = new NpgsqlConnection(restricted.ConnectionString);
            await TestProtectedOperationsAsync(client, "PostgreSql");
        }
        finally
        {
            foreach (var statement in AbaTenantRlsPolicies.Remove("PostgreSql", "public", TableName))
                await ExecAsync(admin, statement);
            await ExecAsync(admin, "DROP TABLE IF EXISTS \"public\".\"aba_ci_rls_rows\"");
            await ExecAsync(admin, $"DROP ROLE IF EXISTS \"{role}\"");
        }
    }

    private static async Task SqlServerRealPolicyAsync(string settings)
    {
        var config = new SqlConnectionStringBuilder(settings) { Pooling = false };
        await using var admin = new SqlConnection(config.ConnectionString);
        await admin.OpenAsync();
        try
        {
            foreach (var statement in AbaTenantRlsPolicies.Remove("SqlServer", "dbo", TableName))
                await ExecAsync(admin, statement);
            await ExecAsync(admin,
                "IF OBJECT_ID(N'dbo.aba_ci_rls_rows', N'U') IS NOT NULL DROP TABLE [dbo].[aba_ci_rls_rows]");
            await ExecAsync(admin,
                "CREATE TABLE [dbo].[aba_ci_rls_rows] (" +
                "[Id] uniqueidentifier NOT NULL PRIMARY KEY, [TenantId] nvarchar(128) NOT NULL, " +
                "[Note] nvarchar(200) NOT NULL)");
            await SeedAsync(admin, "SqlServer");
            foreach (var statement in AbaTenantRlsPolicies.Install("SqlServer", "dbo", TableName))
                await ExecAsync(admin, statement);

            await using var client = new SqlConnection(config.ConnectionString);
            await TestProtectedOperationsAsync(client, "SqlServer");
        }
        finally
        {
            foreach (var statement in AbaTenantRlsPolicies.Remove("SqlServer", "dbo", TableName))
                await ExecAsync(admin, statement);
            await ExecAsync(admin,
                "IF OBJECT_ID(N'dbo.aba_ci_rls_rows', N'U') IS NOT NULL DROP TABLE [dbo].[aba_ci_rls_rows]");
        }
    }

    private static async Task SeedAsync(DbConnection c, string engine)
    {
        var table = engine == "PostgreSql"
            ? "\"public\".\"aba_ci_rls_rows\"" : "[dbo].[aba_ci_rls_rows]";
        var cols = engine == "PostgreSql"
            ? "(\"Id\", \"TenantId\", \"Note\")" : "([Id], [TenantId], [Note])";
        await ExecAsync(c, $"INSERT INTO {table} {cols} VALUES " +
            $"('{AlphaId}', 'tenant-alpha', 'alpha-original'), " +
            $"('{BetaId}', 'tenant-beta', 'beta-original')");
    }

    private static async Task TestProtectedOperationsAsync(DbConnection connection, string engine)
    {
        // No tenant context must see zero rows even with raw SQL.
        await connection.OpenAsync();
        Assert.Equal(0, await ScalarAsync(connection, null,
            "SELECT COUNT(*) FROM " + Table(engine)));
        await connection.CloseAsync();

        var result = await AbaTenantSqlScope.RunAsync(connection, engine, new Tenant("tenant-alpha"),
            async (conn, tx, token) =>
            {
                var count = await ScalarAsync(conn, tx,
                    "SELECT COUNT(*) FROM " + Table(engine));
                var foreignCount = await ScalarAsync(conn, tx,
                    "SELECT COUNT(*) FROM " + Table(engine) +
                    " WHERE " + Column(engine, "TenantId") + " = 'tenant-beta'");
                var denied = await ExecuteSqlAsync(conn, tx, "UPDATE " + Table(engine) +
                    " SET " + Column(engine, "Note") + " = 'raw-tamper' WHERE " +
                    Column(engine, "Id") + $" = '{BetaId}'");

                // This DbContext has no EF tenant filters at all: only the
                // database RLS policy can block ExecuteUpdate/ExecuteDelete.
                var builder = new DbContextOptionsBuilder<RowDb>();
                if (engine == "PostgreSql")
                    builder.UseNpgsql((NpgsqlConnection)conn);
                else
                    builder.UseSqlServer((SqlConnection)conn);
                await using var db = new RowDb(builder.Options);
                await db.Database.UseTransactionAsync(tx, token);
                var bulkUpdated = await db.Rows.Where(x => x.TenantId == "tenant-beta")
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.Note, "bulk-tamper"), token);
                var bulkDeleted = await db.Rows.Where(x => x.TenantId == "tenant-beta")
                    .ExecuteDeleteAsync(token);
                var ownUpdated = await ExecuteSqlAsync(conn, tx, "UPDATE " + Table(engine) +
                    " SET " + Column(engine, "Note") + " = 'alpha-updated' WHERE " +
                    Column(engine, "Id") + $" = '{AlphaId}'");
                return (count, foreignCount, denied, bulkUpdated, bulkDeleted, ownUpdated);
            });
        Assert.Equal((1, 0, 0, 0, 0, 1), result);

        // A new scoped tenant has no access to the previous tenant's row.
        var beta = await AbaTenantSqlScope.RunAsync(connection, engine, new Tenant("tenant-beta"),
            async (conn, tx, _) => await ScalarAsync(conn, tx,
                "SELECT COUNT(*) FROM " + Table(engine) +
                " WHERE " + Column(engine, "Id") + $" = '{AlphaId}'"));
        Assert.Equal(0, beta);

        // Even a direct forged INSERT is rejected by the database policy.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            AbaTenantSqlScope.RunAsync(connection, engine, new Tenant("tenant-alpha"),
                async (conn, tx, _) => await ExecuteSqlAsync(conn, tx,
                    "INSERT INTO " + Table(engine) +
                    " (" + Column(engine, "Id") + ", " + Column(engine, "TenantId") +
                    ", " + Column(engine, "Note") + ") VALUES ('" +
                    Guid.NewGuid() + "', 'tenant-beta', 'forged')")));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            AbaTenantSqlScope.RunAsync(connection, engine, new Tenant(null),
                (conn, tx, _) => Task.FromResult(0)));

        // The SQLServer pool / PostgreSQL transaction must not retain tenant A.
        await connection.OpenAsync();
        Assert.Equal(0, await ScalarAsync(connection, null,
            "SELECT COUNT(*) FROM " + Table(engine)));
        await connection.CloseAsync();
    }

    private static string Table(string engine) =>
        engine == "PostgreSql" ? "\"public\".\"aba_ci_rls_rows\"" : "[dbo].[aba_ci_rls_rows]";
    private static string Column(string engine, string name) =>
        engine == "PostgreSql" ? "\"" + name + "\"" : "[" + name + "]";

    private static async Task ExecAsync(DbConnection c, string sql)
    {
        await using var command = c.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecuteSqlAsync(DbConnection c, DbTransaction? transaction, string sql)
    {
        await using var command = c.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(DbConnection c, DbTransaction? transaction, string sql)
    {
        await using var command = c.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
