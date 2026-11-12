using Microsoft.Extensions.Configuration;

namespace Aba.Starter.Platform.Persistence;

public enum AbaDatabaseProvider { None, PostgreSql, SqlServer }

/// <summary>
/// Provider selection is configuration, not a business-domain decision.
/// No connection string is bundled with the generated project.
/// </summary>
public sealed record AbaDatabaseOptions(AbaDatabaseProvider Provider, string? ConnectionString)
{
    public bool Enabled => Provider != AbaDatabaseProvider.None;

    public static AbaDatabaseOptions FromConfiguration(IConfiguration config)
    {
        var name = config["Platform:Database:Provider"];
        if (string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase))
            return new(AbaDatabaseProvider.None, null);

        var provider = name.Trim().ToLowerInvariant() switch
        {
            "postgresql" or "postgres" => AbaDatabaseProvider.PostgreSql,
            "sqlserver" or "mssql" => AbaDatabaseProvider.SqlServer,
            _ => throw new InvalidOperationException(
                "Platform:Database:Provider must be None, PostgreSql, or SqlServer.")
        };
        var connection = config.GetConnectionString("AbaDatabase");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "ConnectionStrings:AbaDatabase must be supplied when a database provider is enabled.");
        return new(provider, connection);
    }
}
