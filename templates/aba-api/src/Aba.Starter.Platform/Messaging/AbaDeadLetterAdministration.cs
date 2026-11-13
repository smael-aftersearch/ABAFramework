using System.Security.Claims;
using Aba.Starter.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;

namespace Aba.Starter.Platform.Messaging;

/// <summary>
/// Opt-in service-only, single-tenant operator boundary. It never registers
/// HTTP endpoints, auto-grants claims or publishes the raw message payload.
/// No replay is attempted until a hash-linked audit request commits.
/// </summary>
public sealed class AbaDeadLetterAdministration(IMessageStore store, AbaAuditLedger audit)
{
    public const string PermissionClaim = "aba_permission";
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

        var actor = authenticated[0].FindFirst("sub")?.Value;
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
        return new(dead.Id, dead.MessageType, dead.ExceptionType, dead.Replayable);
    }

    /// <summary>
    /// Only a specifically inspected envelope with matching types may be replayed.
    /// This marks the Wolverine database dead letter as replayable; processing
    /// occurs later through its durability agent and may re-fail.
    /// </summary>
    public async Task<bool> RequestReplayAsync(Guid envelopeId,
        string expectedMessageType, string expectedExceptionType,
        ClaimsPrincipal operatorPrincipal, string changeTicket,
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

        await audit.AppendAsync(new("system", actor, "deadletter.replay.request",
            "allowed", changeTicket), ct);
        try
        {
            await store.DeadLetters.ReplayAsync(
                new DeadLetterEnvelopeQuery([envelopeId]), ct);
            await audit.AppendAsync(new("system", actor, "deadletter.replay.queued",
                "allowed", changeTicket), ct);
        }
        catch
        {
            // Even an unsuccessful replay request must leave an audit record.
            await audit.AppendAsync(new("system", actor, "deadletter.replay.failed",
                "failed", changeTicket), CancellationToken.None);
            throw;
        }
        return true;
    }

    public sealed record AbaDeadLetterSummary(
        Guid Id, string MessageType, string ExceptionType, bool Replayable);
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
