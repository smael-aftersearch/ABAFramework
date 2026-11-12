using Aba.Starter.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>
/// A separately signed off-DB checkpoint secures a historical audit prefix
/// even as new legitimate events arrive after the checkpoint was issued.
/// </summary>
public sealed class AuditAnchoredCheckpointIntegrationTests
{
    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task OfflineSignaturePinsHistoricalPrefixThroughAppendsAndDetectsTailDeletion(
        string provider, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        Assert.False(string.IsNullOrWhiteSpace(connection));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Platform:Database:Provider"] = provider,
            ["ConnectionStrings:AbaDatabase"] = connection,
            ["Platform:Audit:Enabled"] = "true",
            ["Platform:Audit:PrimaryKeyId"] = "ledger-v1",
            ["Platform:Audit:Keys:ledger-v1"] =
                Convert.ToBase64String(Enumerable.Repeat((byte)0x51, 32).ToArray())
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAbaPersistence(AbaDatabaseOptions.FromConfiguration(config));
        services.AddAbaAudit(config, true);
        await using var sp = services.BuildServiceProvider();
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            for (var attempt = 0; ; attempt++)
            {
                try { await db.Database.MigrateAsync(); break; }
                catch (Exception ex) when (attempt < 29 &&
                    (ex is System.Data.Common.DbException or TimeoutException))
                { await Task.Delay(2000); }
            }
        }

        var tenant = "ci-audit-anchored-" + Guid.NewGuid().ToString("N");
        var signer = Enumerable.Repeat((byte)0x80, 32).ToArray();
        var ledger = sp.GetRequiredService<AbaAuditLedger>();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ledger.CheckpointCandidateAsync(tenant));
            await ledger.AppendAsync(new(tenant, "ci-actor", "checkpoint.first", "allowed", "ci-1"));
            var candidate = await ledger.CheckpointCandidateAsync(tenant);
            Assert.Equal(1, candidate.Sequence);
            var signed = AbaAuditCheckpointCodec.Sign(candidate, "separate-signer",
                signer, DateTimeOffset.UtcNow.ToUniversalTime());
            Assert.True((await ledger.VerifyAnchoredPrefixAsync(signed, signer)).Valid);
            await ledger.AppendAsync(new(tenant, "ci-actor", "checkpoint.second", "allowed", "ci-2"));
            var later = await ledger.VerifyAnchoredPrefixAsync(signed, signer);
            Assert.True(later.Valid, later.Reason);
            Assert.Equal(2, later.VerifiedRecords);
            Assert.False((await ledger.VerifyAnchoredPrefixAsync(signed with {
                HeadDigest = new string('f', 64)
            }, signer)).Valid);
            Assert.False((await ledger.VerifyAnchoredPrefixAsync(signed,
                Enumerable.Repeat((byte)0x81, 32).ToArray())).Valid);

            // Delete the anchored prefix only: the independently signed digest
            // must reveal the rollback, even though the DB still has newer rows.
            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
                Assert.Equal(1, await db.AuditRecords.Where(x =>
                    x.TenantId == tenant && x.Sequence == 1).ExecuteDeleteAsync());
            }
            var result = await ledger.VerifyAnchoredPrefixAsync(signed, signer);
            Assert.False(result.Valid);
        }
        finally
        {
            await using var scope = sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            await db.AuditRecords.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
        }
    }
}
