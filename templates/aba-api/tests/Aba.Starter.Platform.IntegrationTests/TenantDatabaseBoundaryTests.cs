using Aba.Starter.Platform.Persistence;
using Aba.Starter.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

/// <summary>
/// Test the reusable tenant EF filter and SaveChanges guard against real
/// PostgreSQL AND SQL Server, not only the in-memory/SQLite unit suite.
/// </summary>
public sealed class TenantDatabaseBoundaryTests
{
    private sealed class TestTenant(string? tenantId) : IRequestTenant
    {
        public string? TenantId => tenantId;
    }

    private sealed class TenantRecord : ITenantOwnedRow
    {
        public Guid Id { get; set; }
        public string TenantId { get; set; } = "";
        public string Label { get; set; } = "";
    }

    private sealed class TenantDb(
        DbContextOptions<TenantDb> options, IRequestTenant tenant)
        : AbaTenantDbContext(options, tenant)
    {
        public DbSet<TenantRecord> Records => Set<TenantRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TenantRecord>(entity =>
            {
                entity.ToTable("aba_ci_tenant_rows");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
                entity.Property(x => x.Label).IsRequired();
            });
            base.OnModelCreating(modelBuilder);
        }
    }

    [Theory]
    [InlineData("PostgreSql", "ABA_TEST_POSTGRESQL")]
    [InlineData("SqlServer", "ABA_TEST_SQLSERVER")]
    public async Task ActualSqlEngineRejectsCrossTenantAccessAndWrites(
        string provider, string variable)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        Assert.False(string.IsNullOrWhiteSpace(connection),
            $"Real integration connection missing: {variable}");

        // Each integration test must provision its own database prerequisites;
        // never depend on another xUnit class happening to migrate first.
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:Database:Provider"] = provider,
                ["ConnectionStrings:AbaDatabase"] = connection
            }).Build();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddAbaPersistence(AbaDatabaseOptions.FromConfiguration(config));
        await using (var providerRoot = services.BuildServiceProvider())
        {
            await using var scope = providerRoot.CreateAsyncScope();
            var platform = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await platform.Database.MigrateAsync();
                    break;
                }
                catch (Exception ex) when (attempt < 29
                    && (ex is System.Data.Common.DbException or TimeoutException))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }

        var optionsBuilder = new DbContextOptionsBuilder<TenantDb>();
        if (provider == "PostgreSql")
            optionsBuilder.UseNpgsql(connection);
        else
            optionsBuilder.UseSqlServer(connection);
        var options = optionsBuilder.Options;

        // Test-owned independent table. This is deliberately NOT a business
        // migration and never uses or changes another context's domain tables.
        await using (var setup = new TenantDb(options, new TestTenant("setup")))
        {
            var sql = provider == "PostgreSql"
                ? "CREATE TABLE IF NOT EXISTS aba_ci_tenant_rows (\"Id\" uuid NOT NULL PRIMARY KEY, \"TenantId\" varchar(128) NOT NULL, \"Label\" text NOT NULL)"
                : "IF OBJECT_ID(N'dbo.aba_ci_tenant_rows', N'U') IS NULL CREATE TABLE dbo.aba_ci_tenant_rows ([Id] uniqueidentifier NOT NULL PRIMARY KEY, [TenantId] nvarchar(128) NOT NULL, [Label] nvarchar(max) NOT NULL)";
            await setup.Database.ExecuteSqlRawAsync(sql);
        }

        var recordId = Guid.NewGuid();
        var alpha = $"tenant-a-{Guid.NewGuid():N}";
        var beta = $"tenant-b-{Guid.NewGuid():N}";

        await using (var first = new TenantDb(options, new TestTenant(alpha)))
        {
            first.Records.Add(new TenantRecord
            {
                Id = recordId,
                Label = "alpha-data"
            });
            await first.SaveChangesAsync();
            Assert.Equal(alpha, (await first.Records.SingleAsync(x => x.Id == recordId)).TenantId);
        }

        await using (var second = new TenantDb(options, new TestTenant(beta)))
        {
            Assert.False(await second.Records.AnyAsync(x => x.Id == recordId));
            var foreign = await second.Records.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == recordId);
            foreign.Label = "tampered-by-beta";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => second.SaveChangesAsync());
        }

        await using (var second = new TenantDb(options, new TestTenant(beta)))
        {
            second.Records.Add(new TenantRecord
            {
                Id = Guid.NewGuid(),
                TenantId = alpha,
                Label = "forged-ownership"
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => second.SaveChangesAsync());
        }

        await using (var missing = new TenantDb(options, new TestTenant(null)))
        {
            Assert.False(await missing.Records.AnyAsync(x => x.Id == recordId));
            missing.Records.Add(new TenantRecord
            {
                Id = Guid.NewGuid(), Label = "without-authorized-tenant"
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => missing.SaveChangesAsync());
        }

        await using (var first = new TenantDb(options, new TestTenant(alpha)))
        {
            Assert.Equal("alpha-data",
                (await first.Records.AsNoTracking().SingleAsync(x => x.Id == recordId)).Label);
            first.Records.Remove(await first.Records.SingleAsync(x => x.Id == recordId));
            await first.SaveChangesAsync();
        }
    }
}
