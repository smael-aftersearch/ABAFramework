using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aba.Starter.Platform.Persistence;

/// <summary>
/// Unsigned checkpoint candidate returned by the database. Do not trust or
/// store it as an integrity anchor until a separate offline custodian signs it.
/// </summary>
public sealed record AbaAuditCheckpointCandidate(string TenantId, long Sequence, string HeadDigest);

/// <summary>
/// V1 signed anchor. Its signing key must NOT be the database ledger HMAC key.
/// Persist in independent immutable storage controlled by Security/Operations.
/// </summary>
public sealed record AbaSignedAuditCheckpoint(
    int Version, string TenantId, long Sequence, string HeadDigest,
    DateTimeOffset IssuedUtc, string KeyId, string Signature);

public static class AbaAuditCheckpointCodec
{
    private static string Payload(AbaSignedAuditCheckpoint item) =>
        string.Join("\n", "ABA-AUDIT-CHECKPOINT-V1", item.TenantId,
            item.Sequence.ToString(CultureInfo.InvariantCulture),
            item.HeadDigest, item.IssuedUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            item.KeyId);

    private static void ValidateKey(ReadOnlySpan<byte> secret)
    {
        if (secret.Length is < 32 or > 128)
            throw new ArgumentException("A separate checkpoint signing key must contain 32-128 bytes.");
    }

    private static void Validate(AbaSignedAuditCheckpoint item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Version != 1 || item.Sequence < 1)
            throw new ArgumentException("Invalid signed audit checkpoint version or sequence.");
        AbaAuditLedger.ValidateIdentifier(item.TenantId, "checkpoint tenant");
        AbaAuditLedger.ValidateIdentifier(item.KeyId, "checkpoint signer key ID", 48);
        if (string.IsNullOrEmpty(item.HeadDigest) || item.HeadDigest.Length != 64 ||
            !item.HeadDigest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            item.IssuedUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid checkpoint digest or UTC timestamp.");
    }

    /// <summary>
    /// Offline signing operation; the web process should only produce an
    /// UNSIGNED candidate and should not hold the separate signing key.
    /// </summary>
    public static AbaSignedAuditCheckpoint Sign(
        AbaAuditCheckpointCandidate candidate, string keyId,
        ReadOnlySpan<byte> independentSigningKey, DateTimeOffset issuedUtc)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateKey(independentSigningKey);
        var item = new AbaSignedAuditCheckpoint(
            1, candidate.TenantId, candidate.Sequence, candidate.HeadDigest,
            issuedUtc, keyId, "");
        Validate(item);
        var signature = HMACSHA256.HashData(independentSigningKey,
            Encoding.UTF8.GetBytes(Payload(item)));
        return item with { Signature = Convert.ToHexStringLower(signature) };
    }

    public static bool Verify(AbaSignedAuditCheckpoint item, ReadOnlySpan<byte> independentSigningKey)
    {
        ValidateKey(independentSigningKey);
        try { Validate(item); }
        catch (ArgumentException) { return false; }
        if (item.Signature?.Length != 64)
            return false;
        byte[] claimed;
        try { claimed = Convert.FromHexString(item.Signature); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(independentSigningKey,
            Encoding.UTF8.GetBytes(Payload(item)));
        return CryptographicOperations.FixedTimeEquals(expected, claimed);
    }
}

public sealed partial class AbaAuditLedger
{
    /// <summary>Returns an UNSIGNED candidate after checking the entire chain.</summary>
    public async Task<AbaAuditCheckpointCandidate> CheckpointCandidateAsync(
        string tenantId, CancellationToken ct = default)
    {
        var result = await VerifyAsync(tenantId, ct: ct);
        if (!result.Valid || result.VerifiedRecords < 1)
            throw new InvalidOperationException(
                "Cannot anchor a nonexistent or invalid audit chain.");
        return new(tenantId, result.VerifiedRecords, result.HeadDigest);
    }

    /// <summary>
    /// Verify the HMAC chain in one snapshot, then require the independently
    /// SIGNED digest at its historical sequence. Later appends remain valid;
    /// removing or rewriting the anchored prefix fails closed.
    /// </summary>
    public async Task<AbaAuditVerification> VerifyAnchoredPrefixAsync(
        AbaSignedAuditCheckpoint checkpoint, ReadOnlyMemory<byte> independentKey,
        CancellationToken ct = default)
    {
        if (!AbaAuditCheckpointCodec.Verify(checkpoint, independentKey.Span))
            return new(false, 0, null, "Invalid independent checkpoint signature.", Genesis);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AbaPlatformDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, ct);
        // A signed historical checkpoint must be verified across the
        // ordered stream without retaining the entire tenant ledger in RAM.
        // The repeatable-read transaction supplies one consistent snapshot.
        var rows = db.AuditRecords.AsNoTracking()
            .Where(x => x.TenantId == checkpoint.TenantId)
            .OrderBy(x => x.Sequence).AsAsyncEnumerable();
        var previous = Genesis;
        long sequence = 0;
        var verifiedCount = 0;
        bool matchedAnchor = false;
        await foreach (var row in rows.WithCancellation(ct))
        {
            sequence++;
            if (row.Sequence != sequence ||
                !MatchDigest(row.PreviousDigest, previous) ||
                !MatchDigest(row.Digest, Hash(row)))
                return new(false, verifiedCount, row.Sequence,
                    "Audit sequence, link or signature changed.", previous);
            previous = row.Digest;
            verifiedCount = checked(verifiedCount + 1);
            if (sequence == checkpoint.Sequence)
            {
                if (!MatchDigest(row.Digest, checkpoint.HeadDigest))
                    return new(false, verifiedCount, row.Sequence,
                        "Anchored audit prefix changed.", previous);
                matchedAnchor = true;
            }
        }
        if (!matchedAnchor)
            return new(false, verifiedCount, null,
                "Anchored audit prefix was truncated.", previous);
        await transaction.CommitAsync(ct);
        return new(true, verifiedCount, null,
            "Verified against signed historical checkpoint.", previous);
    }
}
