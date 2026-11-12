using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aba.Starter.Platform.Persistence;

public static class AbaPersistenceServices
{
    public static IServiceCollection AddAbaPersistence(
        this IServiceCollection services, AbaDatabaseOptions database)
    {
        if (!database.Enabled) return services;

        services.AddDbContext<AbaPlatformDbContext>(options =>
        {
            if (database.Provider == AbaDatabaseProvider.PostgreSql)
                options.UseNpgsql(database.ConnectionString);
            else if (database.Provider == AbaDatabaseProvider.SqlServer)
                options.UseSqlServer(database.ConnectionString);
            else
                throw new InvalidOperationException("Unsupported database provider.");
        }, optionsLifetime: ServiceLifetime.Singleton);

        services.AddScoped<EfIdempotencyStore>();
        services.AddHealthChecks().AddDbContextCheck<AbaPlatformDbContext>(
            name: "aba-database", tags: ["ready"]);
        return services;
    }
}

/// <summary>
/// A durable, database-enforced idempotency claim. Scope is caller/tenant-owned; never
/// use the same scope across unrelated tenants or commands.
/// </summary>
public sealed class EfIdempotencyStore(AbaPlatformDbContext db)
{
    /// <summary>
    /// Transaction-safe alternative to TryClaimAsync: the receipt and all EF changes
    /// performed by <paramref name="work"/> use ONE database transaction. Never
    /// run external side effects or a second DbContext transaction inside work.
    /// </summary>
    public async Task<bool> ExecuteOnceInTransactionAsync(
        string scope,
        string key,
        Func<AbaPlatformDbContext, CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(work);
        if (scope.Length > 128 || key.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(key), "Scope/key exceed configured limits.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (await db.IdempotencyReceipts.AsNoTracking()
                .AnyAsync(x => x.Scope == scope && x.Key == key, ct))
                return false;

            db.IdempotencyReceipts.Add(new IdempotencyReceipt
            {
                Scope = scope,
                Key = key,
                CreatedUtc = DateTimeOffset.UtcNow
            });
            await work(db, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex)
        {
            // PostgreSQL/SQL Server mark a failed transaction unusable until
            // rollback; never query again inside that same transaction.
            await tx.RollbackAsync(CancellationToken.None);
            var receiptConflict = ex.Entries.Any(e =>
                e.Entity is IdempotencyReceipt r && r.Scope == scope && r.Key == key);
            db.ChangeTracker.Clear();
            if (receiptConflict && await db.IdempotencyReceipts.AsNoTracking()
                .AnyAsync(x => x.Scope == scope && x.Key == key, ct))
                return false;
            throw;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> TryClaimAsync(string scope, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (scope.Length > 128 || key.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(key), "Scope/key exceed configured limits.");

        if (await db.IdempotencyReceipts.AsNoTracking()
            .AnyAsync(x => x.Scope == scope && x.Key == key, ct))
            return false;

        var record = new IdempotencyReceipt { Scope = scope, Key = key, CreatedUtc = DateTimeOffset.UtcNow };
        db.IdempotencyReceipts.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            // Do not suppress unrelated errors (unavailable database, wrong schema, etc.).
            if (await db.IdempotencyReceipts.AsNoTracking()
                .AnyAsync(x => x.Scope == scope && x.Key == key, ct))
                return false;
            throw;
        }
    }
}
