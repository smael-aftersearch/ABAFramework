using Aba.Starter.Platform.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aba.Starter.Platform.Persistence.Migrations;

[DbContext(typeof(AbaPlatformDbContext))]
[Migration("202611120001_InitialTechnicalStore")]
public sealed class InitialTechnicalStore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "aba_idempotency_receipts",
            columns: table => new
            {
                Scope = table.Column<string>(maxLength: 128, nullable: false),
                Key = table.Column<string>(maxLength: 200, nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_aba_idempotency_receipts",
                x => new { x.Scope, x.Key }));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "aba_idempotency_receipts");
    }
}
