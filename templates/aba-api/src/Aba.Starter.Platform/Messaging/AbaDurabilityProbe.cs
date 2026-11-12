using Aba.Starter.Platform.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Aba.Starter.Platform.Messaging;

/// <summary>
/// Internal technical probe for integration-test verification of durable delivery.
/// It is not a business integration contract and no HTTP endpoint publishes it.
/// </summary>
public sealed record AbaDurabilityProbe(Guid Id);

public static class AbaDurabilityProbeHandler
{
    public static void Handle(AbaDurabilityProbe message, AbaPlatformDbContext db)
    {
        db.IdempotencyReceipts.Add(new IdempotencyReceipt
        {
            Scope = "aba-durability-probe",
            Key = message.Id.ToString("N"),
            CreatedUtc = DateTimeOffset.UtcNow
        });
        // Wolverine EF transactional middleware performs SaveChanges and outbox flushing.
    }
}

/// <summary>
/// Technical transaction probe, not a domain command. Its EF row and emitted event
/// must either both commit, or both roll back. Never use it for real business data.
/// </summary>
public sealed record AbaTransactionalOutboxProbe(Guid Id, bool FailAfterPublish);

public static class AbaTransactionalOutboxProbeHandler
{
    public static async Task Handle(
        AbaTransactionalOutboxProbe command, AbaPlatformDbContext db, IMessageBus bus)
    {
        db.IdempotencyReceipts.Add(new IdempotencyReceipt
        {
            Scope = "aba-outbox-transaction",
            Key = command.Id.ToString("N"),
            CreatedUtc = DateTimeOffset.UtcNow
        });

        // This is deliberately published *before* the injected failure.
        // Wolverine's EF unit of work must buffer the outgoing envelope and
        // commit both record and outbox together.
        await bus.PublishAsync(new AbaDurabilityProbe(command.Id));
        if (command.FailAfterPublish)
            throw new InvalidOperationException("Injected pre-commit fault for outbox atomicity test.");
    }
}


/// <summary>
/// Test-only technical poison probe. A message is retried/dead-lettered until
/// an EF receipt indicates the environment was fixed; never used by products.
/// </summary>
public sealed record AbaPoisonProbe(Guid Id);

public sealed class AbaPoisonProbeException : Exception
{
    public AbaPoisonProbeException() : base("Synthetic technical poison message") { }
}

public static class AbaPoisonProbeHandler
{
    public static void Handle(AbaPoisonProbe message, AbaPlatformDbContext db)
    {
        var key = message.Id.ToString("N");
        if (!db.IdempotencyReceipts.Any(x => x.Scope == "aba-poison-fixed" && x.Key == key))
            throw new AbaPoisonProbeException();

        db.IdempotencyReceipts.Add(new IdempotencyReceipt
        {
            Scope = "aba-poison-handled",
            Key = key,
            CreatedUtc = DateTimeOffset.UtcNow
        });
    }
}


/// <summary>
/// Technical, CI-only ACK-loss probe. A receipt commits independently of the
/// Wolverine handler ACK. After killing the worker before it returns, the
/// durable queue must redeliver the message without duplicating the effect.
/// Not a business event or permission to crash a production handler.
/// </summary>
public sealed record AbaCrashAckProbe(Guid Id);

public static class AbaCrashAckProbeHandler
{
    public static void RequireIsolatedCrashMarker(
        string marker, bool ci, bool explicitlyEnabled)
    {
        if (!ci || !explicitlyEnabled || string.IsNullOrWhiteSpace(marker) ||
            !Path.GetFullPath(marker).StartsWith(Path.GetFullPath(Path.GetTempPath()),
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "ACK fault injection is forbidden outside an explicit disposable CI drill.");
    }

    public static async Task Handle(
        AbaCrashAckProbe message, IServiceScopeFactory scopeFactory,
        CancellationToken ct)
    {
        var marker = Environment.GetEnvironmentVariable("ABA_CI_CRASH_ACK_MARKER");
        // Check the safety gate BEFORE touching any database row.
        if (!string.IsNullOrWhiteSpace(marker))
            RequireIsolatedCrashMarker(marker,
                Environment.GetEnvironmentVariable("CI") == "true",
                Environment.GetEnvironmentVariable("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS") == "yes");
        var key = message.Id.ToString("N");
        await using var separate = scopeFactory.CreateAsyncScope();
        var receipts = separate.ServiceProvider.GetRequiredService<EfIdempotencyStore>();
        // Every handler execution writes a new attempt receipt; the independent
        // idempotent effect receipt has exactly one winner across re-deliveries.
        await receipts.TryClaimAsync("aba-crash-attempt-" + key,
            Guid.NewGuid().ToString("N"), ct);
        var first = await receipts.TryClaimAsync("aba-crash-effect", key, ct);

        if (string.IsNullOrWhiteSpace(marker))
            return;
        // The receipt is committed, but Wolverine has not acknowledged the
        // envelope: this handler has not yet returned to the runtime.
        File.WriteAllText(marker, first
            ? "effect-committed-before-ack" : "duplicate-committed-before-ack");
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }
}
