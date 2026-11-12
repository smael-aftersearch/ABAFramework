using Aba.Starter.Platform.Persistence;

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
