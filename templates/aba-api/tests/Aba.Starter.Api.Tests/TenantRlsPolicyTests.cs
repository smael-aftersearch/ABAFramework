using Aba.Starter.Platform.Persistence;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Aba.Starter.Api.Tests;

/// <summary>Offline migration-DSL invariants; real provider access has its own CI suite.</summary>
public sealed class TenantRlsPolicyTests
{
    [Fact]
    public void PostgresRequiresForcedRlsAndExplicitWriteCheck()
    {
        var sql = AbaTenantRlsPolicies.Install("PostgreSql", "inventory", "products");
        Assert.Equal(3, sql.Count);
        Assert.Contains("ENABLE ROW LEVEL SECURITY", sql[0]);
        Assert.Contains("FORCE ROW LEVEL SECURITY", sql[1]);
        Assert.Contains("WITH CHECK", sql[2]);
        Assert.Contains("current_setting('aba.tenant_id', true)", sql[2]);
        Assert.Contains("NULLIF(", sql[2]);
    }

    [Fact]
    public void SqlServerRequiresReadFilterAndInsertUpdateBlockPredicates()
    {
        var sql = AbaTenantRlsPolicies.Install("SqlServer", "inventory", "products");
        Assert.Equal(3, sql.Count);
        Assert.Contains("SCHEMABINDING", sql[1]);
        Assert.Contains("SESSION_CONTEXT(N'aba_tenant_id')", sql[1]);
        Assert.Contains("ADD FILTER PREDICATE", sql[2]);
        Assert.Contains("AFTER INSERT", sql[2]);
        Assert.Contains("AFTER UPDATE", sql[2]);
    }

    [Theory]
    [InlineData("public", "bad; DROP TABLE users")]
    [InlineData("bad space", "products")]
    [InlineData("a.b", "products")]
    [InlineData("dbo", "bad]table")]
    public void RejectsUnreviewedSqlIdentifiers(string schema, string table)
    {
        Assert.Throws<ArgumentException>(() =>
            AbaTenantRlsPolicies.Install("PostgreSql", schema, table));
        Assert.Throws<ArgumentException>(() =>
            AbaTenantRlsPolicies.Install("SqlServer", schema, table));
    }

    [Fact]
    public void RemovalDropsOnlyContextPolicyNotSharedPredicateFunction()
    {
        var remove = AbaTenantRlsPolicies.Remove("SqlServer", "dbo", "orders");
        Assert.Single(remove);
        Assert.Contains("DROP SECURITY POLICY IF EXISTS", remove[0]);
        Assert.DoesNotContain("DROP FUNCTION", remove[0]);
    }

    [Fact]
    public async Task MissingTenantRejectsBeforeOpeningConnection()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await AbaTenantSqlScope.RunAsync(conn, "SqlServer", new FakeTenant(null),
                (c, t, ct) => Task.FromResult(1)));
        Assert.Equal(System.Data.ConnectionState.Closed, conn.State);
    }

    [Fact]
    public async Task FailedOpenClosesPartiallyOpenedTenantConnectionBeforeItCanBeReused()
    {
        await using var connection = new PartiallyOpenedConnection();
        var invoked = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AbaTenantSqlScope.RunAsync(connection, "PostgreSql", new FakeTenant("tenant-alpha"),
                (c, t, ct) =>
                {
                    invoked = true;
                    return Task.FromResult(1);
                }));
        Assert.Equal("synthetic open failure", error.Message);
        Assert.Equal(1, connection.OpenCalls);
        Assert.Equal(1, connection.CloseCalls);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.False(invoked); // A failed OpenAsync never reaches business SQL.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("../foreign")]
    [InlineData("tenant other")]
    public async Task InvalidTenantCannotReachSqlEvenWhenProviderCouldOpen(string? tenantId)
    {
        await using var connection = new PartiallyOpenedConnection();
        var invoked = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            AbaTenantSqlScope.RunAsync(connection, "PostgreSql", new FakeTenant(tenantId),
                (c, t, ct) =>
                {
                    invoked = true;
                    return Task.FromResult(1);
                }));
        Assert.Equal(0, connection.OpenCalls);
        Assert.Equal(0, connection.CloseCalls);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.False(invoked);
    }

    // A provider can transition to Open before OpenAsync reports a failure.
    // This disposable fake models exactly that failure boundary; the real SQL
    // provider happy/foreign-tenant cases remain in RowLevelSecurityTests.
    private sealed class PartiallyOpenedConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        public int OpenCalls { get; private set; }
        public int CloseCalls { get; private set; }
        [AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "disposable";
        public override string DataSource => "disposable";
        public override string ServerVersion => "0";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) =>
            throw new NotSupportedException();
        public override void Open() => throw new NotSupportedException();
        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            OpenCalls++;
            _state = ConnectionState.Open;
            return Task.FromException(new InvalidOperationException("synthetic open failure"));
        }
        public override void Close()
        {
            CloseCalls++;
            _state = ConnectionState.Closed;
        }
        public override Task CloseAsync()
        {
            Close();
            return Task.CompletedTask;
        }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() =>
            throw new NotSupportedException();
    }

    private sealed record FakeTenant(string? TenantId) : Aba.Starter.Platform.Security.IRequestTenant;
}
