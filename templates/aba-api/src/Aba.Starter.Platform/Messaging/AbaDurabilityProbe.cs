using Aba.Starter.Platform.Persistence;
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
