using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Aba.Starter.Platform.Security;

namespace Aba.Starter.Platform.Persistence;

/// <summary>
/// Provider-specific, reviewable DDL for an owning context's tenant-owned TABLE.
/// Deployment migrations must install this DDL with separate migration privileges.
/// It deliberately does not install policies on arbitrary business tables at startup.
/// </summary>
public static class AbaTenantRlsPolicies
{
    private static string Identifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 48 ||
            !char.IsAsciiLetter(value[0]) ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("RLS SQL identifier must be 1-48 ASCII letters/digits/underscore and start with a letter.");
        return value;
    }

    public static IReadOnlyList<string> Install(string provider, string schema, string table,
        string tenantColumn = "TenantId")
    {
        var engine = Normalize(provider);
        schema = Identifier(schema);
        table = Identifier(table);
        tenantColumn = Identifier(tenantColumn);
        if (engine == "PostgreSql")
        {
            var target = $"\"{schema}\".\"{table}\"";
            var column = $"\"{tenantColumn}\"";
            var guard = $"{column} = NULLIF(current_setting('aba.tenant_id', true), '')";
            return [
                $"ALTER TABLE {target} ENABLE ROW LEVEL SECURITY",
                $"ALTER TABLE {target} FORCE ROW LEVEL SECURITY",
                $"CREATE POLICY \"aba_tenant_isolation\" ON {target} FOR ALL USING ({guard}) WITH CHECK ({guard})"
            ];
        }

        var fullTable = $"[{schema}].[{table}]";
        var predicate = $"[aba_security].[fn_tenant_access]([{tenantColumn}])";
        var policy = $"[aba_security].[aba_{schema}_{table}_rls]";
        return [
            "IF SCHEMA_ID(N'aba_security') IS NULL EXEC(N'CREATE SCHEMA [aba_security]')",
            "CREATE OR ALTER FUNCTION [aba_security].[fn_tenant_access](@TenantId nvarchar(128)) " +
                "RETURNS TABLE WITH SCHEMABINDING AS RETURN SELECT 1 AS allowed " +
                "WHERE @TenantId = CONVERT(nvarchar(128), SESSION_CONTEXT(N'aba_tenant_id'))",
            $"CREATE SECURITY POLICY {policy} " +
                $"ADD FILTER PREDICATE {predicate} ON {fullTable}, " +
                $"ADD BLOCK PREDICATE {predicate} ON {fullTable} AFTER INSERT, " +
                $"ADD BLOCK PREDICATE {predicate} ON {fullTable} AFTER UPDATE WITH (STATE = ON)"
        ];
    }

    public static IReadOnlyList<string> Remove(string provider, string schema, string table)
    {
        var engine = Normalize(provider);
        schema = Identifier(schema);
        table = Identifier(table);
        return engine == "PostgreSql"
            ? [$"DROP POLICY IF EXISTS \"aba_tenant_isolation\" ON \"{schema}\".\"{table}\""]
            : [$"DROP SECURITY POLICY IF EXISTS [aba_security].[aba_{schema}_{table}_rls]"];
    }

    internal static string Normalize(string provider) =>
        provider?.Trim().ToLowerInvariant() switch
        {
            "postgresql" or "postgres" => "PostgreSql",
            "sqlserver" or "mssql" => "SqlServer",
            _ => throw new ArgumentException("RLS supports only PostgreSql or SqlServer.")
        };
}

/// <summary>
/// A trusted, transaction-bound tenant context for raw SQL and EF ExecuteUpdate.
/// Caller owns authorization of IRequestTenant; using tenant headers is forbidden.
/// Always uses a previously closed connection; SQL Server SESSION_CONTEXT is
/// cleared before returning a pooled connection. PostgreSQL uses SET LOCAL.
/// </summary>
public static class AbaTenantSqlScope
{
    public static async Task<T> RunAsync<T>(
        DbConnection connection, string provider, IRequestTenant tenant,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(action);
        var engine = AbaTenantRlsPolicies.Normalize(provider);
        var tenantId = tenant.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 128 ||
            tenantId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.')))
            throw new UnauthorizedAccessException("An authenticated, valid tenant claim is required.");
        if (connection.State != ConnectionState.Closed)
            throw new InvalidOperationException("Tenant SQL scope requires a fresh closed connection.");
        if (engine == "SqlServer" && connection is not SqlConnection)
            throw new ArgumentException("SqlServer RLS requires a SqlConnection.");

        try
        {
            // Even an unsuccessful OpenAsync can leave a provider connection
            // partially open. Always close it before returning to the caller.
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await SetTenantAsync(connection, transaction, engine, tenantId, cancellationToken);
                var result = await action(connection, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch (DbException) { /* preserve original failure */ }
                throw;
            }
            finally
            {
                if (engine == "SqlServer")
                {
                    try
                    {
                        await using var clear = connection.CreateCommand();
                        clear.CommandText =
                            "EXEC sys.sp_set_session_context @key=N'aba_tenant_id', @value=NULL";
                        await clear.ExecuteNonQueryAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // Do not return a tainted SQL Server session to the pool.
                        SqlConnection.ClearPool((SqlConnection)connection);
                        throw;
                    }
                }
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task SetTenantAsync(DbConnection connection, DbTransaction transaction,
        string engine, string tenant, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = engine == "PostgreSql"
            ? "SELECT set_config('aba.tenant_id', @tenant, true)"
            : "EXEC sys.sp_set_session_context @key=N'aba_tenant_id', @value=@tenant";
        var p = cmd.CreateParameter();
        p.ParameterName = "@tenant";
        p.Value = tenant;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
