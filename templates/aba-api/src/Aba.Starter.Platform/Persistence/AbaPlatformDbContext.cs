using Microsoft.EntityFrameworkCore;

namespace Aba.Starter.Platform.Persistence;

/// <summary>Technical infrastructure state only; never store business domain entities here.</summary>
public sealed class AbaPlatformDbContext(DbContextOptions<AbaPlatformDbContext> options) : DbContext(options)
{
    public DbSet<IdempotencyReceipt> IdempotencyReceipts => Set<IdempotencyReceipt>();
    public DbSet<AbaAuditRecord> AuditRecords => Set<AbaAuditRecord>();
    public DbSet<AbaDeadLetterInspectionReceipt> DeadLetterInspectionReceipts =>
        Set<AbaDeadLetterInspectionReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AbaAuditRecord>(entity =>
        {
            entity.ToTable("aba_audit_records");
            entity.HasKey(x => new { x.TenantId, x.Sequence });
            entity.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(16).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.KeyId).HasMaxLength(48).IsRequired();
            entity.Property(x => x.PreviousDigest).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Digest).HasMaxLength(64).IsRequired();
            entity.Property(x => x.OccurredUtc).IsRequired();
        });
        modelBuilder.Entity<IdempotencyReceipt>(entity =>
        {
            entity.ToTable("aba_idempotency_receipts");
            entity.HasKey(x => new { x.Scope, x.Key });
            entity.Property(x => x.Scope).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Key).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedUtc).IsRequired();
        });
        modelBuilder.Entity<AbaDeadLetterInspectionReceipt>(entity =>
        {
            entity.ToTable("aba_deadletter_inspection_receipts");
            entity.HasKey(x => x.EnvelopeId);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.MessageType).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.ExceptionType).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.OperatorSubject).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ChangeTicket).HasMaxLength(128).IsRequired();
            entity.Property(x => x.IssuedUtc).IsRequired();
            entity.Property(x => x.ExpiresUtc).IsRequired();
        });
    }
}

public sealed class IdempotencyReceipt
{
    public string Scope { get; set; } = "";
    public string Key { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}

/// <summary>
/// Short-lived, one-time proof that one operator inspected one exact dead-letter
/// envelope. Only the SHA-256 token digest is persisted.
/// </summary>
public sealed class AbaDeadLetterInspectionReceipt
{
    public Guid EnvelopeId { get; set; }
    public string TokenHash { get; set; } = "";
    public string MessageType { get; set; } = "";
    public string ExceptionType { get; set; } = "";
    public string OperatorSubject { get; set; } = "";
    public string ChangeTicket { get; set; } = "";
    public DateTimeOffset IssuedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
}
