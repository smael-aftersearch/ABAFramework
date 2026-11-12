using Aba.Starter.Platform.Persistence;
using Aba.Starter.Platform.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aba.Starter.Api.Tests;

/// <summary>Tests that per-request EF filters and write guards never reuse a tenant identity.</summary>
public sealed class TenantPersistenceTests
{
    private sealed class FixedTenant(string? id) : IRequestTenant
    {
        public string? TenantId => id;
    }

    private sealed class TenantRow : ITenantOwnedRow
    {
        public Guid Id { get; set; }
        public string TenantId { get; set; } = "";
        public string Value { get; set; } = "";
    }

    private sealed class TenantTestDb(
        DbContextOptions<TenantTestDb> options, IRequestTenant tenant)
        : AbaTenantDbContext(options, tenant)
    {
        public DbSet<TenantRow> Rows => Set<TenantRow>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<TenantRow>(entity => entity.HasKey(x => x.Id));
            base.OnModelCreating(builder);
        }
    }

    [Fact]
    public async Task QueriesAreTenantScopedAcrossDifferentContextInstances()
    {
        await using var sqlite = new SqliteConnection("Data Source=:memory:");
        await sqlite.OpenAsync();
        var options = new DbContextOptionsBuilder<TenantTestDb>().UseSqlite(sqlite).Options;
        var firstId = Guid.NewGuid();

        await using (var alpha = new TenantTestDb(options, new FixedTenant("tenant-alpha")))
        {
            await alpha.Database.EnsureCreatedAsync();
            alpha.Rows.Add(new TenantRow { Id = firstId, Value = "alpha-data" });
            await alpha.SaveChangesAsync();
            Assert.Equal("tenant-alpha", (await alpha.Rows.SingleAsync()).TenantId);
        }

        await using (var beta = new TenantTestDb(options, new FixedTenant("tenant-beta")))
        {
            Assert.Empty(await beta.Rows.ToListAsync());
            beta.Rows.Add(new TenantRow { Id = Guid.NewGuid(), Value = "beta-data" });
            await beta.SaveChangesAsync();
            var own = await beta.Rows.SingleAsync();
            Assert.Equal("tenant-beta", own.TenantId);
        }

        await using (var alpha = new TenantTestDb(options, new FixedTenant("tenant-alpha")))
        {
            Assert.Equal(firstId, (await alpha.Rows.SingleAsync()).Id);
        }
    }

    [Fact]
    public async Task ReadingWithIgnoreQueryFiltersDoesNotPermitCrossTenantWrites()
    {
        await using var sqlite = new SqliteConnection("Data Source=:memory:");
        await sqlite.OpenAsync();
        var options = new DbContextOptionsBuilder<TenantTestDb>().UseSqlite(sqlite).Options;
        var otherId = Guid.NewGuid();
        await using (var alpha = new TenantTestDb(options, new FixedTenant("alpha")))
        {
            await alpha.Database.EnsureCreatedAsync();
            alpha.Rows.Add(new TenantRow { Id = otherId, Value = "unchanged" });
            await alpha.SaveChangesAsync();
        }

        await using (var beta = new TenantTestDb(options, new FixedTenant("beta")))
        {
            // EF allows explicit query-filter bypass for administrative use.
            // The separate save guard must still block cross-tenant mutation.
            var foreign = await beta.Rows.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == otherId);
            foreign.Value = "stolen";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => beta.SaveChangesAsync());
        }

        await using (var alpha = new TenantTestDb(options, new FixedTenant("alpha")))
        {
            Assert.Equal("unchanged", (await alpha.Rows.AsNoTracking().SingleAsync()).Value);
        }
    }

    [Fact]
    public async Task WritesWithoutAuthenticatedTenantOrWithForgedTenantAreRejected()
    {
        await using var sqlite = new SqliteConnection("Data Source=:memory:");
        await sqlite.OpenAsync();
        var options = new DbContextOptionsBuilder<TenantTestDb>().UseSqlite(sqlite).Options;

        await using (var missing = new TenantTestDb(options, new FixedTenant(null)))
        {
            await missing.Database.EnsureCreatedAsync();
            missing.Rows.Add(new TenantRow { Id = Guid.NewGuid(), Value = "unauthorized" });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => missing.SaveChangesAsync());
        }

        await using (var beta = new TenantTestDb(options, new FixedTenant("beta")))
        {
            beta.Rows.Add(new TenantRow
            {
                Id = Guid.NewGuid(), TenantId = "alpha", Value = "forged"
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => beta.SaveChangesAsync());
        }
    }
}
