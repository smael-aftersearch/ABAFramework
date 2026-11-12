using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Aba.Starter.Platform.Persistence;

/// <summary>
/// A technical, PII-minimized chain. Business owners choose which operations
/// are auditable and prove the actor is authorized for the specified tenant.
/// This service does not read HTTP headers or grant membership.
/// </summary>
public sealed class AbaAuditRecord
{
    public string TenantId { get; set; } = "";
    public long Sequence { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string PreviousDigest { get; set; } = "";
    public string Digest { get; set; } = "";
}

public sealed record AbaAuditWrite(
    string TenantId, string ActorId, string Action, string Outcome, string CorrelationId);

public sealed record AbaAuditVerification(
    bool Valid, int VerifiedRecords, long? BrokenSequence, string Reason, string HeadDigest);

/// <summary>
/// HMAC keys must be injected by a secret provider, not checked into an
/// appsettings file. Retain old key IDs for verification after rotation.
/// </summary>
public sealed class AbaAuditOptions
{
    private readonly IReadOnlyDictionary<string, byte[]> _keys;
    public string PrimaryKeyId { get; }
    private AbaAuditOptions(string primary, Dictionary<string, byte[]> keys)
    {
        PrimaryKeyId = primary;
        _keys = keys;
    }

    internal byte[] KeyFor(string keyId) =>
        _keys.TryGetValue(keyId, out var key)
            ? key : throw new InvalidOperationException(
                "Audit HMAC verification key is unavailable; old key material must be retained.");

    public static AbaAuditOptions FromConfiguration(IConfiguration configuration)
    {
        var primary = configuration["Platform:Audit:PrimaryKeyId"];
        AbaAuditLedger.ValidateIdentifier(primary, nameof(PrimaryKeyId), 48);
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var item in configuration.GetSection("Platform:Audit:Keys").GetChildren())
        {
            AbaAuditLedger.ValidateIdentifier(item.Key, "Audit key ID", 48);
            byte[] key;
            try { key = Convert.FromBase64String(item.Value ?? ""); }
            catch (FormatException)
            {
                throw new InvalidOperationException("Audit key is not valid base64.");
            }
            if (key.Length < 32 || key.Length > 128)
                throw new InvalidOperationException("Audit keys must decode to 32-128 bytes.");
            keys.Add(item.Key, key);
        }
        if (primary is null || !keys.ContainsKey(primary))
            throw new InvalidOperationException("Audit primary key ID must reference an injected key.");
        return new AbaAuditOptions(primary, keys);
    }
}

public static class AbaAuditRegistration
{
    public static IServiceCollection AddAbaAudit(
        this IServiceCollection services, IConfiguration config, bool databaseEnabled)
    {
        if (!config.GetValue<bool>("Platform:Audit:Enabled"))
            return services;
        if (!databaseEnabled)
            throw new InvalidOperationException("Audit ledger requires a relational database.");
        // Fail at startup, not on the first incident, if the secret store is empty.
        services.AddSingleton(AbaAuditOptions.FromConfiguration(config));
        services.AddSingleton<AbaAuditLedger>();
        return services;
    }
}

/// <summary>
/// Append-only API over serializable EF transactions; row tampering without
/// the HMAC keys is detected. Root/terminal truncation needs an independently
/// retained head digest, accepted by VerifyAsync(expectedHeadDigest).
/// </summary>
public sealed partial class AbaAuditLedger(IServiceScopeFactory scopes, AbaAuditOptions options)
{
    private const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    public static void ValidateIdentifier(string? text, string field, int maxLength = 128)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength ||
            text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':')))
            throw new ArgumentException($"Audit {field} must use a non-empty stable opaque identifier.");
    }

    public static void Validate(AbaAuditWrite value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateIdentifier(value.TenantId, nameof(value.TenantId));
        ValidateIdentifier(value.ActorId, nameof(value.ActorId));
        ValidateIdentifier(value.Action, nameof(value.Action));
        if (value.Outcome is not ("allowed" or "denied" or "failed"))
            throw new ArgumentException("Audit outcome must be allowed, denied or failed.");
        ValidateIdentifier(value.CorrelationId, nameof(value.CorrelationId));
    }

    private string Hash(AbaAuditRecord record)
    {
        var body = string.Join("\n", record.TenantId,
            record.Sequence.ToString(CultureInfo.InvariantCulture),
            record.OccurredUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            record.ActorId, record.Action, record.Outcome, record.CorrelationId,
            record.KeyId, record.PreviousDigest);
        return Convert.ToHexString(
            HMACSHA256.HashData(options.KeyFor(record.KeyId), Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();
    }

    private static bool MatchDigest(string actual, string expected)
    {
        if (actual.Length != 64 || expected.Length != 64)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected));
    }

    public async Task<AbaAuditRecord> AppendAsync(AbaAuditWrite command,
        CancellationToken ct = default)
    {
        Validate(command);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, ct);
                var last = await db.AuditRecords.AsNoTracking()
                    .Where(x => x.TenantId == command.TenantId)
                    .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
                var record = new AbaAuditRecord
                {
                    TenantId = command.TenantId,
                    Sequence = last is null ? 1 : checked(last.Sequence + 1),
                    // PostgreSQL timestamp(tz) stores microseconds rather than
                    // .NET's 100ns ticks. Normalize BEFORE signing so SQL Server
                    // and PostgreSQL round-trip to identical canonical bytes.
                    OccurredUtc = MicrosecondUtcNow(),
                    ActorId = command.ActorId,
                    Action = command.Action,
                    Outcome = command.Outcome,
                    CorrelationId = command.CorrelationId,
                    KeyId = options.PrimaryKeyId,
                    PreviousDigest = last?.Digest ?? Genesis
                };
                record.Digest = Hash(record);
                db.AuditRecords.Add(record);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return record;
            }
            catch (Exception ex) when (attempt < 9 && Retryable(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), ct);
            }
        }
        throw new InvalidOperationException("Audit concurrency retry limit exceeded.");
    }

    private static DateTimeOffset MicrosecondUtcNow()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.UtcDateTime.Ticks % 10));
    }

    private static bool Retryable(Exception error)
    {
        // EF Core can wrap provider deadlock/serialization errors inside its
        // execution strategy's InvalidOperationException, not only DbUpdateException.
        if (error.InnerException is not null && error is not System.Data.Common.DbException)
            return Retryable(error.InnerException);
        return error switch
        {
            PostgresException pg when pg.SqlState is "40001" or "40P01" or "23505" => true,
            SqlException sql when sql.Number is 1205 or 2601 or 2627 or 3960 => true,
            _ => false
        };
    }

    public async Task<AbaAuditVerification> VerifyAsync(
        string tenantId, string? expectedHeadDigest = null, CancellationToken ct = default)
    {
        ValidateIdentifier(tenantId, nameof(tenantId));
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        var records = await db.AuditRecords.AsNoTracking()
            .Where(x => x.TenantId == tenantId).OrderBy(x => x.Sequence)
            .ToListAsync(ct);
        var previous = Genesis;
        long sequence = 0;
        foreach (var record in records)
        {
            sequence++;
            if (record.Sequence != sequence ||
                !MatchDigest(record.PreviousDigest, previous) ||
                !MatchDigest(record.Digest, Hash(record)))
                return new(false, (int)(sequence - 1), record.Sequence,
                    "Audit digest, sequence or link mismatch.", previous);
            previous = record.Digest;
        }
        if (expectedHeadDigest is not null && !MatchDigest(previous, expectedHeadDigest))
            return new(false, records.Count, null,
                "Audit head does not match the independent checkpoint.", previous);
        return new(true, records.Count, null, "Verified", previous);
    }
}
