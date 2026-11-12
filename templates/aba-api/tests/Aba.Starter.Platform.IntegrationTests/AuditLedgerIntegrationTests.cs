using Aba.Starter.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>
/// Real PostgreSQL and SQL Server tests: independently check HMAC integrity,
/// tampering, suffix truncation with off-DB checkpoint and key rotation.
/// All values are synthetic technical identifiers, not business data.
/// </summary>
public sealed class AuditLedgerIntegrationTests
{
    private static readonly string Key1 =
        Convert.ToBase64String(Enumerable.Repeat((byte)47, 32).ToArray());
    private static readonly string Key2 =
        Convert.ToBase64String(Enumerable.Repeat((byte)61, 32).ToArray());

    private static ServiceProvider Build(string provider, string connection,
        string primary, bool retainOld)
    {
        var fields = new Dictionary<string, string?>
        {
            ["Platform:Database:Provider"] = provider,
            ["ConnectionStrings:AbaDatabase"] = connection,
            ["Platform:Audit:Enabled"] = "true",
            ["Platform:Audit:PrimaryKeyId"] = primary,
            ["Platform:Audit:Keys:key-v2"] = Key2
        };
        if (retainOld) fields["Platform:Audit:Keys:key-v1"] = Key1;
        var config = new ConfigurationBuilder().AddInMemoryCollection(fields).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaPersistence(AbaDatabaseOptions.FromConfiguration(config));
        services.AddAbaAudit(config, databaseEnabled: true);
        return services.BuildServiceProvider();
    }

    private static async Task EnsureMigratedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                break;
            }
            catch (Exception ex) when (attempt < 29 &&
                (ex is System.Data.Common.DbException or TimeoutException))
            {
                await Task.Delay(2000);
            }
        }
    }

    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task HmacChainDetectsTamperAndKeyRotationOnBothSqlProviders(
        string engine, string envVar)
    {
        var connection = Environment.GetEnvironmentVariable(envVar);
        Assert.False(string.IsNullOrWhiteSpace(connection));
        var tenant = "ci-audit-" + Guid.NewGuid().ToString("N");

        await using var first = Build(engine, connection!, "key-v1", retainOld: true);
        await EnsureMigratedAsync(first);
        var audit = first.GetRequiredService<AbaAuditLedger>();
        var one = await audit.AppendAsync(new(tenant, "actor-1", "claim.create",
            "allowed", "request-1"));
        var two = await audit.AppendAsync(new(tenant, "actor-2", "claim.deny",
            "denied", "request-2"));
        Assert.Equal(1, one.Sequence);
        Assert.Equal(2, two.Sequence);
        Assert.Equal(one.Digest, two.PreviousDigest);

        var verified = await audit.VerifyAsync(tenant, two.Digest);
        Assert.True(verified.Valid, verified.Reason);
        Assert.Equal(2, verified.VerifiedRecords);
        Assert.False((await audit.VerifyAsync(tenant, new string('f', 64))).Valid);

        // New writes use v2; older rows remain verifiable while v1 is held.
        await using var rotated = Build(engine, connection!, "key-v2", retainOld: true);
        var later = await rotated.GetRequiredService<AbaAuditLedger>().AppendAsync(
            new(tenant, "actor-2", "claim.review", "allowed", "request-3"));
        Assert.Equal("key-v2", later.KeyId);
        Assert.True((await audit.VerifyAsync(tenant, later.Digest)).Valid);

        // Removing the old key breaks proof for historical entries (fail closed).
        await using var noOldKey = Build(engine, connection!, "key-v2", retainOld: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            noOldKey.GetRequiredService<AbaAuditLedger>().VerifyAsync(tenant));

        // Mutating a record without the external HMAC key must be detected.
        await using (var scope = first.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            Assert.Equal(1, await db.AuditRecords
                .Where(x => x.TenantId == tenant && x.Sequence == 1)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Outcome, "failed")));
        }
        var checkedChain = await audit.VerifyAsync(tenant);
        Assert.False(checkedChain.Valid);
        Assert.Equal(1, checkedChain.BrokenSequence);

        // Only synthetic data from this specific tenant is removed.
        await using (var scope = first.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            await db.AuditRecords.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task ConcurrentAuditAppendsUseDistinctMonotonicSequences(
        string engine, string envVar)
    {
        var connection = Environment.GetEnvironmentVariable(envVar);
        Assert.False(string.IsNullOrWhiteSpace(connection));
        var tenant = "ci-audit-race-" + Guid.NewGuid().ToString("N");
        await using var services = Build(engine, connection!, "key-v1", retainOld: true);
        await EnsureMigratedAsync(services);
        var ledger = services.GetRequiredService<AbaAuditLedger>();
        var results = await Task.WhenAll(Enumerable.Range(1, 5).Select(n =>
            ledger.AppendAsync(new(tenant, "actor-1", "work.done", "allowed",
                "request-" + n))));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 },
            results.Select(x => x.Sequence).OrderBy(x => x));
        var check = await ledger.VerifyAsync(tenant);
        Assert.True(check.Valid, check.Reason);
        Assert.Equal(5, check.VerifiedRecords);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        await db.AuditRecords.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
    }
}
