using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monetail.Api.Persistence.Migrations;

/// <summary>既存の認証情報を保持し、管理者専用の任意メモだけを追加する。</summary>
[DbContext(typeof(ApiDbContext))]
[Migration("20261104020000_AddAccountAdminNote")]
public sealed class AddAccountAdminNote : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateTable(
        name: "auth_account_admin_notes",
        columns: table => new
        {
            AuthUserId = table.Column<string>(type: "TEXT", nullable: false),
            AdminNote = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_auth_account_admin_notes", row => row.AuthUserId);
            table.ForeignKey("FK_auth_account_admin_notes_AspNetUsers_AuthUserId", row => row.AuthUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
        });

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("auth_account_admin_notes");
}
