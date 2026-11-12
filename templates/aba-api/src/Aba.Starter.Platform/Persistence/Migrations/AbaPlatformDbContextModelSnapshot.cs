using Aba.Starter.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Aba.Starter.Platform.Persistence.Migrations;

[DbContext(typeof(AbaPlatformDbContext))]
public sealed class AbaPlatformDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyReceipt>(b =>
        {
            b.ToTable("aba_idempotency_receipts");
            b.HasKey(x => new { x.Scope, x.Key });
            b.Property(x => x.Scope).HasMaxLength(128).IsRequired();
            b.Property(x => x.Key).HasMaxLength(200).IsRequired();
            b.Property(x => x.CreatedUtc).IsRequired();
        });
    }
}
