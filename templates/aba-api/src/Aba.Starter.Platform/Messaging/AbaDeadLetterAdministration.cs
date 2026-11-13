using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;

namespace Aba.Starter.Platform.Messaging;

/// <summary>
/// Opt-in service-only, single-tenant operator boundary. It never registers
/// HTTP endpoints, auto-grants claims or publishes the raw message payload.
/// No replay is attempted until a hash-linked audit request commits.
/// </summary>
public sealed class AbaDeadLetterAdministration(
    IMessageStore store, AbaAuditLedger audit, AbaPlatformDbContext db)
{
    public const string PermissionClaim = "aba_permission";
    public static readonly TimeSpan InspectionReceiptLifetime = TimeSpan.FromMinutes(5);
    private const string InspectPermission = "deadletters:inspect";
    private const string ReplayPermission = "deadletters:replay";

    private static string Authorize(ClaimsPrincipal principal, string permission, string ticket)
    {
        // Never compose permission and subject from different identities:
        // ClaimsPrincipal.HasClaim searches even unauthenticated identities.
        var authenticated = principal?.Identities
            .Where(identity => identity.IsAuthenticated).ToArray();
        if (authenticated is not { Length: 1 } ||
            !authenticated[0].HasClaim(PermissionClaim, permission))
            throw new UnauthorizedAccessException("An authenticated, explicitly authorized operator is required.");

        // A single authenticated identity can still contain conflicting sub
        // claims. Never choose the first one for a signed operator audit.
        var subjects = authenticated[0].FindAll("sub").ToArray();
        if (subjects.Length != 1)
            throw new UnauthorizedAccessException("Exactly one operator subject claim is required.");
        var actor = subjects[0].Value;
        AbaAuditLedger.ValidateIdentifier(actor, "operator subject");
        AbaAuditLedger.ValidateIdentifier(ticket, "change ticket");
        if (ticket.Length < 6)
            throw new ArgumentException("A nontrivial operational change ticket ID is required.");
        return actor!;
    }

    private static void CheckId(Guid envelopeId)
    {
        if (envelopeId == Guid.Empty)
            throw new ArgumentException("A specific nonempty dead-letter envelope ID is required.");
    }

    /// <summary>Metadata only: no body or exception message can leave this API.</summary>
    public async Task<AbaDeadLetterSummary> InspectAsync(Guid envelopeId,
        ClaimsPrincipal operatorPrincipal, string changeTicket, CancellationToken ct = default)
    {
        CheckId(envelopeId);
        var actor = Authorize(operatorPrincipal, InspectPermission, changeTicket);
        // Fail closed if the HMAC ledger cannot be persisted.
        await audit.AppendAsync(new("system", actor, "deadletter.inspect",
            "allowed", changeTicket), ct);

        var dead = await store.DeadLetters.DeadLetterEnvelopeByIdAsync(envelopeId);
        if (dead is null)
            throw new KeyNotFoundException("The requested dead letter was not found.");
        var replayToken = dead.Replayable
            ? null
            : await IssueInspectionReceiptAsync(dead.Id, dead.MessageType,
                dead.ExceptionType, actor, changeTicket, ct);
        return new(dead.Id, dead.MessageType, dead.ExceptionType, dead.Replayable, replayToken);
    }

    /// <summary>
    /// Only a specifically inspected envelope with matching types may be replayed.
    /// This marks the Wolverine database dead letter as replayable; processing
    /// occurs later through its durability agent and may re-fail.
    /// </summary>
    public async Task<bool> RequestReplayAsync(Guid envelopeId,
        string expectedMessageType, string expectedExceptionType,
        string inspectionToken, ClaimsPrincipal operatorPrincipal, string changeTicket,
        CancellationToken ct = default)
    {
        CheckId(envelopeId);
        var actor = Authorize(operatorPrincipal, ReplayPermission, changeTicket);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMessageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedExceptionType);
        var dead = await store.DeadLetters.DeadLetterEnvelopeByIdAsync(envelopeId);
        if (dead is null)
            throw new KeyNotFoundException("The specified dead letter no longer exists.");
        if (!string.Equals(dead.MessageType, expectedMessageType, StringComparison.Ordinal)
            || !string.Equals(dead.ExceptionType, expectedExceptionType, StringComparison.Ordinal))
            throw new InvalidOperationException("The dead letter changed since inspection.");
        if (dead.Replayable)
            return false;

        var tokenHash = HashInspectionToken(inspectionToken);
        var now = DateTimeOffset.UtcNow;
        var consumed = await db.DeadLetterInspectionReceipts
            .Where(x => x.EnvelopeId == envelopeId
                && x.TokenHash == tokenHash
                && x.MessageType == expectedMessageType
                && x.ExceptionType == expectedExceptionType
                && x.OperatorSubject == actor
                && x.ChangeTicket == changeTicket
                && x.IssuedUtc <= now
                && x.ExpiresUtc > now
                && x.ConsumedUtc == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ConsumedUtc, now), ct);
        if (consumed != 1)
            throw new InvalidOperationException(
                "A valid, unexpired, unused inspection receipt for this operator and ticket is required.");

        try
        {
            await audit.AppendAsync(new("system", actor, "deadletter.replay.request",
                "allowed", changeTicket), ct);
        }
        catch
        {
            await ReleaseReceiptAsync(envelopeId, tokenHash, actor, changeTicket);
            throw;
        }

        try
        {
            await store.DeadLetters.ReplayAsync(
                new DeadLetterEnvelopeQuery([envelopeId]), ct);
        }
        catch
        {
            // Release only when Wolverine confirms the envelope remains
            // non-replayable. If the outcome is uncertain, fail closed and
            // require operator review instead of allowing a concurrent retry.
            var current = await store.DeadLetters.DeadLetterEnvelopeByIdAsync(envelopeId);
            if (current is not null && !current.Replayable)
                await ReleaseReceiptAsync(envelopeId, tokenHash, actor, changeTicket);
            await audit.AppendAsync(new("system", actor, "deadletter.replay.failed",
                "failed", changeTicket), CancellationToken.None);
            throw;
        }

        // If this audit write fails, keep the one-time receipt consumed: the
        // replay has already been queued and must not be submitted again.
        await audit.AppendAsync(new("system", actor, "deadletter.replay.queued",
            "allowed", changeTicket), ct);
        return true;
    }

    public sealed record AbaDeadLetterSummary(
        Guid Id, string MessageType, string ExceptionType, bool Replayable,
        string? ReplayToken)
    {
        // The replay token is a transient capability returned to the approved
        // caller. Keep ordinary logging/string formatting safe by default.
        public override string ToString()
            => $"AbaDeadLetterSummary {{ Id = {Id}, MessageType = {MessageType}, " +
               $"ExceptionType = {ExceptionType}, Replayable = {Replayable}, " +
               "ReplayToken = [REDACTED] }";
    }

    private async Task<string> IssueInspectionReceiptAsync(Guid envelopeId,
        string messageType, string exceptionType, string actor, string changeTicket,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageType) || messageType.Length > 1024
            || string.IsNullOrWhiteSpace(exceptionType) || exceptionType.Length > 1024)
            throw new InvalidOperationException("Dead-letter metadata exceeds the safe inspection limit.");

        var token = NewInspectionToken();
        var tokenHash = HashInspectionToken(token);
        var issued = DateTimeOffset.UtcNow;
        var expires = issued.Add(InspectionReceiptLifetime);
        var consumedRetryAfter = issued.Subtract(InspectionReceiptLifetime);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            // One row per envelope means a fresh inspection invalidates any
            // previously issued, unused token. A consumed receipt stays
            // exclusive for one safety window after consumption. InspectAsync
            // has already re-read Wolverine and will only refresh it after
            // that window while the envelope is still non-replayable.
            var replaced = await db.DeadLetterInspectionReceipts
                .Where(x => x.EnvelopeId == envelopeId
                    && (x.ConsumedUtc == null
                        || (x.ConsumedUtc.HasValue
                            && x.ConsumedUtc.Value <= consumedRetryAfter)))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.TokenHash, tokenHash)
                    .SetProperty(x => x.MessageType, messageType)
                    .SetProperty(x => x.ExceptionType, exceptionType)
                    .SetProperty(x => x.OperatorSubject, actor)
                    .SetProperty(x => x.ChangeTicket, changeTicket)
                    .SetProperty(x => x.IssuedUtc, issued)
                    .SetProperty(x => x.ExpiresUtc, expires)
                    .SetProperty(x => x.ConsumedUtc, (DateTimeOffset?)null), ct);
            if (replaced == 1)
                return token;

            var exists = await db.DeadLetterInspectionReceipts.AsNoTracking()
                .AnyAsync(x => x.EnvelopeId == envelopeId, ct);
            if (exists)
                throw new InvalidOperationException(
                    "A replay request is already in progress for this envelope.");

            var receipt = new AbaDeadLetterInspectionReceipt
            {
                EnvelopeId = envelopeId,
                TokenHash = tokenHash,
                MessageType = messageType,
                ExceptionType = exceptionType,
                OperatorSubject = actor,
                ChangeTicket = changeTicket,
                IssuedUtc = issued,
                ExpiresUtc = expires
            };
            db.DeadLetterInspectionReceipts.Add(receipt);
            try
            {
                await db.SaveChangesAsync(ct);
                return token;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Another inspection inserted this envelope's unique row.
                // Its next retry replaces that unconsumed receipt atomically.
                db.Entry(receipt).State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Could not issue a dead-letter inspection receipt.");
    }

    private async Task ReleaseReceiptAsync(Guid envelopeId, string tokenHash,
        string actor, string changeTicket)
    {
        await db.DeadLetterInspectionReceipts
            .Where(x => x.EnvelopeId == envelopeId
                && x.TokenHash == tokenHash
                && x.OperatorSubject == actor
                && x.ChangeTicket == changeTicket
                && x.ConsumedUtc != null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ConsumedUtc, (DateTimeOffset?)null), CancellationToken.None);
    }

    private static string NewInspectionToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashInspectionToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Length > 128)
            throw new ArgumentException("Inspection token is malformed.", nameof(token));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}

public static class AbaDeadLetterRegistration
{
    public static IServiceCollection AddAbaDeadLetterAdministration(
        this IServiceCollection services, IConfiguration configuration, bool databaseEnabled)
    {
        if (!configuration.GetValue<bool>("Platform:Messaging:DeadLetterOperatorEnabled"))
            return services;
        if (!databaseEnabled || !configuration.GetValue<bool>("Platform:Messaging:Enabled"))
            throw new InvalidOperationException("Dead-letter operations require durable Wolverine messaging.");
        if (!configuration.GetValue<bool>("Platform:Audit:Enabled"))
            throw new InvalidOperationException("Dead-letter operations require a tamper-evident audit ledger.");
        if (configuration.GetValue<bool>("Platform:Tenancy:Enabled"))
            throw new InvalidOperationException(
                "Dead-letter operations are disabled with tenancy until tenant-specific store authorization is implemented.");
        services.AddScoped<AbaDeadLetterAdministration>();
        return services;
    }
}
