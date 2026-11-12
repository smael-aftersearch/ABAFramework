using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aba.Starter.Platform.Persistence;

/// <summary>For `dotnet ef`: choose ABA_DB_PROVIDER and ABA_DB_CONNECTION from the environment.</summary>
public sealed class AbaPlatformDbContextFactory : IDesignTimeDbContextFactory<AbaPlatformDbContext>
{
    public AbaPlatformDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("ABA_DB_PROVIDER");
        var connection = Environment.GetEnvironmentVariable("ABA_DB_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set ABA_DB_CONNECTION for EF design-time operations.");

        var builder = new DbContextOptionsBuilder<AbaPlatformDbContext>();
        switch (provider?.Trim().ToLowerInvariant())
        {
            case "postgresql":
            case "postgres":
                builder.UseNpgsql(connection);
                break;
            case "sqlserver":
            case "mssql":
                builder.UseSqlServer(connection);
                break;
            default:
                throw new InvalidOperationException("ABA_DB_PROVIDER must be PostgreSql or SqlServer.");
        }
        return new AbaPlatformDbContext(builder.Options);
    }
}
