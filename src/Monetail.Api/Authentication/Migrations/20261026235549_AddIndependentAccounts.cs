using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monetail.Api.Authentication.Migrations
{
    /// <summary>人物名をコピーせず独立表示名を追加し、新規発行の人物指定を不要にする。</summary>
    public partial class AddIndependentAccounts : Migration
    {
        /// <summary>既存値を保持して表示名列と発行履歴の任意人物コードを追加する。</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UserCode",
                table: "auth_account_creation_requests",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <summary>旧スキーマに戻す。人物未指定の履歴は旧版では表現できない。</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<int>(
                name: "UserCode",
                table: "auth_account_creation_requests",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
