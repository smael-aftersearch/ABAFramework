using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aba.Starter.Platform.Persistence.Migrations;

[DbContext(typeof(AbaPlatformDbContext))]
[Migration("202611130001_AuditLedger")]
public sealed class AuditLedger : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.CreateTable(
            name: "aba_audit_records",
            columns: t => new
            {
                TenantId = t.Column<string>(maxLength: 128, nullable: false),
                Sequence = t.Column<long>(nullable: false),
                OccurredUtc = t.Column<DateTimeOffset>(nullable: false),
                ActorId = t.Column<string>(maxLength: 128, nullable: false),
                Action = t.Column<string>(maxLength: 128, nullable: false),
                Outcome = t.Column<string>(maxLength: 16, nullable: false),
                CorrelationId = t.Column<string>(maxLength: 128, nullable: false),
                KeyId = t.Column<string>(maxLength: 48, nullable: false),
                PreviousDigest = t.Column<string>(maxLength: 64, nullable: false),
                Digest = t.Column<string>(maxLength: 64, nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_aba_audit_records",
                x => new { x.TenantId, x.Sequence }));
    }

    protected override void Down(MigrationBuilder builder) =>
        builder.DropTable(name: "aba_audit_records");
}
