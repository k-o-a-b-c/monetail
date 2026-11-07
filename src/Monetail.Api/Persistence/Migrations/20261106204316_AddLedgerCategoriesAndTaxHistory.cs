using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Monetail.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerCategoriesAndTaxHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category_order_revision",
                table: "ledgers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // 既存家計簿にも独立したUUID v4を付与する。新規作成はLedgerの初期値で発行する。
            migrationBuilder.Sql("""
                UPDATE ledgers SET category_order_revision =
                    lower(hex(randomblob(6))) || '4' || substr(lower(hex(randomblob(2))), 2) ||
                    substr('89ab', abs(random() % 4) + 1, 1) || substr(lower(hex(randomblob(8))), 2);
                """);

            migrationBuilder.CreateTable(
                name: "tax_categories",
                columns: table => new
                {
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_categories", x => x.code);
                    table.CheckConstraint("CK_tax_categories_code", "code IN ('Standard','ReducedFood','ReducedNewspaper','Exempt')");
                });

            migrationBuilder.CreateTable(
                name: "ledger_categories",
                columns: table => new
                {
                    ledger_id = table.Column<string>(type: "TEXT", nullable: false),
                    category_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "BINARY"),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    display_order = table.Column<int>(type: "INTEGER", nullable: false),
                    tax_category_code = table.Column<string>(type: "TEXT", nullable: false),
                    income_expense = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_categories", x => new { x.ledger_id, x.category_id });
                    table.CheckConstraint("CK_ledger_categories_enabled", "is_enabled IN (0,1)");
                    table.CheckConstraint("CK_ledger_categories_income_expense", "income_expense IN ('Income','Expense')");
                    table.CheckConstraint("CK_ledger_categories_name", "length(name) BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_ledger_categories_order", "display_order >= 0");
                    table.ForeignKey(
                        name: "FK_ledger_categories_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ledger_categories_tax_categories_tax_category_code",
                        column: x => x.tax_category_code,
                        principalTable: "tax_categories",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_rate_history",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    tax_category_code = table.Column<string>(type: "TEXT", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    revision = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rate_history", x => x.id);
                    table.CheckConstraint("CK_tax_rate_history_from", "typeof(valid_from) = 'text' AND length(valid_from) = 10 AND valid_from GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND valid_from >= '0001-01-01' AND COALESCE(date(valid_from, '+0 days') = valid_from, 0)");
                    table.CheckConstraint("CK_tax_rate_history_period", "valid_to IS NULL OR valid_from <= valid_to");
                    table.CheckConstraint("CK_tax_rate_history_rate", "(tax_category_code = 'Exempt' AND rate IS NULL) OR (tax_category_code <> 'Exempt' AND rate IS NOT NULL)");
                    table.CheckConstraint("CK_tax_rate_history_to", "valid_to IS NULL OR (typeof(valid_to) = 'text' AND length(valid_to) = 10 AND valid_to GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND valid_to >= '0001-01-01' AND COALESCE(date(valid_to, '+0 days') = valid_to, 0))");
                    table.ForeignKey(
                        name: "FK_tax_rate_history_tax_categories_tax_category_code",
                        column: x => x.tax_category_code,
                        principalTable: "tax_categories",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "tax_categories",
                columns: new[] { "code", "name" },
                values: new object[,]
                {
                    { "Exempt", "非課税" },
                    { "ReducedFood", "食品軽減" },
                    { "ReducedNewspaper", "新聞軽減" },
                    { "Standard", "標準" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_categories_order",
                table: "ledger_categories",
                columns: new[] { "ledger_id", "display_order", "category_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_categories_tax_category_code",
                table: "ledger_categories",
                column: "tax_category_code");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_categories_name",
                table: "ledger_categories",
                columns: new[] { "ledger_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_rate_history_category_period",
                table: "tax_rate_history",
                columns: new[] { "tax_category_code", "valid_from", "valid_to" });

            // 期間は両端を含む。税区分ごとに、終了日なしの行も含めて書込時に検証する。
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_tax_rate_history_overlap_insert
                BEFORE INSERT ON tax_rate_history
                FOR EACH ROW
                WHEN EXISTS (
                    SELECT 1 FROM tax_rate_history AS existing
                    WHERE existing.tax_category_code = NEW.tax_category_code
                      AND (existing.valid_to IS NULL OR NEW.valid_from <= existing.valid_to)
                      AND (NEW.valid_to IS NULL OR existing.valid_from <= NEW.valid_to)
                )
                BEGIN
                    SELECT RAISE(ABORT, 'tax_rate_period_overlap');
                END;
                """);

            // 期間は両端を含む。税区分ごとに、終了日なしの行も含めて書込時に検証する。
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_tax_rate_history_overlap_update
                BEFORE UPDATE ON tax_rate_history
                FOR EACH ROW
                WHEN EXISTS (
                    SELECT 1 FROM tax_rate_history AS existing
                    WHERE existing.tax_category_code = NEW.tax_category_code AND existing.id <> OLD.id
                      AND (existing.valid_to IS NULL OR NEW.valid_from <= existing.valid_to)
                      AND (NEW.valid_to IS NULL OR existing.valid_from <= NEW.valid_to)
                )
                BEGIN
                    SELECT RAISE(ABORT, 'tax_rate_period_overlap');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 新マスタのデータは失われる。実データの復旧には停止バックアップを使用する。
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_tax_rate_history_overlap_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_tax_rate_history_overlap_insert;");
            migrationBuilder.DropTable(
                name: "ledger_categories");

            migrationBuilder.DropTable(
                name: "tax_rate_history");

            migrationBuilder.DropTable(
                name: "tax_categories");

            // 親表の再構築で既存の参照・トリガーを巻き込まない。
            migrationBuilder.Sql("ALTER TABLE ledgers DROP COLUMN category_order_revision;");
        }
    }
}
