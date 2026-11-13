using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aba.Starter.Platform.Persistence.Migrations;

[DbContext(typeof(AbaPlatformDbContext))]
[Migration("202611130002_DeadLetterInspectionReceipts")]
public sealed class DeadLetterInspectionReceipts : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.CreateTable(
            name: "aba_deadletter_inspection_receipts",
            columns: t => new
            {
                EnvelopeId = t.Column<Guid>(nullable: false),
                TokenHash = t.Column<string>(maxLength: 64, nullable: false),
                MessageType = t.Column<string>(maxLength: 1024, nullable: false),
                ExceptionType = t.Column<string>(maxLength: 1024, nullable: false),
                OperatorSubject = t.Column<string>(maxLength: 128, nullable: false),
                ChangeTicket = t.Column<string>(maxLength: 128, nullable: false),
                IssuedUtc = t.Column<DateTimeOffset>(nullable: false),
                ExpiresUtc = t.Column<DateTimeOffset>(nullable: false),
                ConsumedUtc = t.Column<DateTimeOffset>(nullable: true)
            },
            constraints: t => t.PrimaryKey("PK_aba_deadletter_inspection_receipts",
                x => x.EnvelopeId));
    }

    protected override void Down(MigrationBuilder builder) =>
        builder.DropTable(name: "aba_deadletter_inspection_receipts");
}
