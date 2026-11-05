using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monetail.Api.Persistence.Migrations;

/// <summary>人物ごとの支払方法順序版と、所有者が変更できない支払方法マスタを追加する。</summary>
[DbContext(typeof(ApiDbContext))]
[Migration("20261105010000_AddLedgerPaymentMethods")]
public sealed class AddLedgerPaymentMethods : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQLite cannot add a NOT NULL column to a populated table without a constant default.
        // Backfill each existing person with an independent random version before any API uses it.
        migrationBuilder.AddColumn<string>(
            name: "payment_order_revision",
            table: "ledger_people",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        // 32 hexadecimal characters, UUID v4 version/variant bits, one random value per row.
        migrationBuilder.Sql("""
            UPDATE ledger_people
            SET payment_order_revision =
                lower(hex(randomblob(6))) ||
                '4' || substr(lower(hex(randomblob(2))), 2) ||
                substr('89ab', abs(random() % 4) + 1, 1) ||
                substr(lower(hex(randomblob(8))), 2);
            """);

        migrationBuilder.CreateTable(
            name: "ledger_payment_methods",
            columns: table => new
            {
                ledger_id = table.Column<string>(type: "TEXT", nullable: false),
                payment_method_id = table.Column<string>(type: "TEXT", nullable: false),
                owner_person_id = table.Column<string>(type: "TEXT", nullable: false),
                name = table.Column<string>(type: "TEXT", nullable: false)
                    .Annotation("Relational:Collation", "BINARY"),
                display_order = table.Column<int>(type: "INTEGER", nullable: false),
                valid_from = table.Column<DateOnly>(type: "TEXT", nullable: true),
                valid_to = table.Column<DateOnly>(type: "TEXT", nullable: true),
                revision = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ledger_payment_methods", row => new { row.ledger_id, row.payment_method_id });
                table.CheckConstraint("CK_ledger_payment_methods_display_order", "display_order >= 0");
                table.CheckConstraint("CK_ledger_payment_methods_period",
                    "valid_from IS NULL OR valid_to IS NULL OR valid_from <= valid_to");
                table.ForeignKey(
                    name: "FK_ledger_payment_methods_ledgers_ledger_id",
                    column: row => row.ledger_id,
                    principalTable: "ledgers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ledger_payment_methods_ledger_people_ledger_id_owner_person_id",
                    columns: row => new { row.ledger_id, row.owner_person_id },
                    principalTable: "ledger_people",
                    principalColumns: new[] { "ledger_id", "person_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_ledger_payment_methods_owner_name",
            table: "ledger_payment_methods",
            columns: new[] { "ledger_id", "owner_person_id", "name" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_ledger_payment_methods_owner_order",
            table: "ledger_payment_methods",
            columns: new[] { "ledger_id", "owner_person_id", "display_order", "payment_method_id" });

        migrationBuilder.Sql("""
            CREATE TRIGGER trg_ledger_payment_methods_owner_immutable
            BEFORE UPDATE OF owner_person_id ON ledger_payment_methods
            FOR EACH ROW
            WHEN OLD.owner_person_id IS NOT NEW.owner_person_id
            BEGIN
                SELECT RAISE(ABORT, 'payment_method_owner_immutable');
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ledger_payment_methods_owner_immutable;");
        migrationBuilder.DropTable(name: "ledger_payment_methods");
        migrationBuilder.DropColumn(name: "payment_order_revision", table: "ledger_people");
    }
}
