using System.Reflection;
using Aba.Starter.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Aba.Starter.Platform.Persistence;

/// <summary>
/// Technical persistence row marker. Implement this on Infrastructure/EF models,
/// never on pure Domain aggregates just to satisfy a persistence concern.
/// </summary>
public interface ITenantOwnedRow
{
    string TenantId { get; set; }
}

/// <summary>
/// Reusable EF Core tenant read/write guard for context-owned infrastructure models.
/// This is not DB-level RLS: raw SQL, administrative access and business-level
/// authorization remain the responsibility of the owning context/deployment.
/// </summary>
public abstract class AbaTenantDbContext : DbContext
{
    private readonly IRequestTenant _tenant;

    protected AbaTenantDbContext(DbContextOptions options, IRequestTenant tenant)
        : base(options)
    {
        _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
    }

    private string? CurrentTenantId => _tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwnedRow).IsAssignableFrom(entityType.ClrType))
                continue;

            var apply = typeof(AbaTenantDbContext).GetMethod(
                nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;
            apply.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwnedRow
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(
            row => CurrentTenantId != null
                && row.TenantId == CurrentTenantId);
    }

    private void ValidateTenantWrites()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not ITenantOwnedRow row
                || entry.State is EntityState.Unchanged or EntityState.Detached)
                continue;

            var tenantId = CurrentTenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new UnauthorizedAccessException(
                    "An authenticated tenant context is required for tenant-owned writes.");

            if (entry.State == EntityState.Added)
            {
                if (string.IsNullOrWhiteSpace(row.TenantId))
                    row.TenantId = tenantId;
                if (!string.Equals(row.TenantId, tenantId, StringComparison.Ordinal))
                    throw new UnauthorizedAccessException(
                        "Cannot insert a record into another tenant.");
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                PropertyEntry tenantProperty = entry.Property(nameof(ITenantOwnedRow.TenantId));
                if (!string.Equals(tenantProperty.OriginalValue as string, tenantId, StringComparison.Ordinal)
                    || !string.Equals(row.TenantId, tenantId, StringComparison.Ordinal)
                    || (entry.State == EntityState.Modified && tenantProperty.IsModified))
                {
                    throw new UnauthorizedAccessException(
                        "Changing or writing another tenant's record is forbidden.");
                }
            }
        }
    }

    public override int SaveChanges()
    {
        ValidateTenantWrites();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateTenantWrites();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
