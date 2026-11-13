using Aba.Starter.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Aba.Starter.Platform.Persistence.Migrations;

[DbContext(typeof(AbaPlatformDbContext))]
public sealed class AbaPlatformDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AbaAuditRecord>(b =>
        {
            b.ToTable("aba_audit_records");
            b.HasKey(x => new { x.TenantId, x.Sequence });
            b.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            b.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
            b.Property(x => x.Action).HasMaxLength(128).IsRequired();
            b.Property(x => x.Outcome).HasMaxLength(16).IsRequired();
            b.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            b.Property(x => x.KeyId).HasMaxLength(48).IsRequired();
            b.Property(x => x.PreviousDigest).HasMaxLength(64).IsRequired();
            b.Property(x => x.Digest).HasMaxLength(64).IsRequired();
            b.Property(x => x.OccurredUtc).IsRequired();
        });
        modelBuilder.Entity<IdempotencyReceipt>(b =>
        {
            b.ToTable("aba_idempotency_receipts");
            b.HasKey(x => new { x.Scope, x.Key });
            b.Property(x => x.Scope).HasMaxLength(128).IsRequired();
            b.Property(x => x.Key).HasMaxLength(200).IsRequired();
            b.Property(x => x.CreatedUtc).IsRequired();
        });
        modelBuilder.Entity<AbaDeadLetterInspectionReceipt>(b =>
        {
            b.ToTable("aba_deadletter_inspection_receipts");
            b.HasKey(x => x.EnvelopeId);
            b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.MessageType).HasMaxLength(1024).IsRequired();
            b.Property(x => x.ExceptionType).HasMaxLength(1024).IsRequired();
            b.Property(x => x.OperatorSubject).HasMaxLength(128).IsRequired();
            b.Property(x => x.ChangeTicket).HasMaxLength(128).IsRequired();
            b.Property(x => x.IssuedUtc).IsRequired();
            b.Property(x => x.ExpiresUtc).IsRequired();
        });
    }
}
