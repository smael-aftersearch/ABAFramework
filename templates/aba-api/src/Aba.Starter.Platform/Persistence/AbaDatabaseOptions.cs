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

    /// <summary>
    /// Production transport guard: do not allow plaintext or server-certificate
    /// bypasses. Validated before the API starts listening, not an arbitrary
    /// attempt to rewrite customer-managed connection strings.
    /// </summary>
    public void ValidateTransportSecurity(bool isDevelopment)
    {
        if (isDevelopment || !Enabled)
            return;

        if (Provider == AbaDatabaseProvider.PostgreSql)
        {
            var connection = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString);
            if (connection.SslMode != Npgsql.SslMode.VerifyFull)
                throw new InvalidOperationException(
                    "Production PostgreSQL requires SSL Mode=VerifyFull and a trusted server certificate.");
        }
        else if (Provider == AbaDatabaseProvider.SqlServer)
        {
            var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString);
            var encryption = connection.Encrypt.ToString();
            if (connection.TrustServerCertificate
                || encryption is not ("True" or "Mandatory" or "Strict"))
                throw new InvalidOperationException(
                    "Production SQL Server requires Encrypt=True or Strict and TrustServerCertificate=False.");
        }
    }

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
