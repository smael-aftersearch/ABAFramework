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
