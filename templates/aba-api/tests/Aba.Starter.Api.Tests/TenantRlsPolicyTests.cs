using Aba.Starter.Platform.Persistence;
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

    private sealed record FakeTenant(string? TenantId) : Aba.Starter.Platform.Security.IRequestTenant;
}
