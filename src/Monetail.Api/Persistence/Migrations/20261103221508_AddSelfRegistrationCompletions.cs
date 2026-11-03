using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monetail.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfRegistrationCompletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_self_registration_completions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_self_registration_completions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_self_registration_completions_CompletedAtUtc",
                table: "auth_self_registration_completions",
                column: "CompletedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_self_registration_completions");
        }
    }
}
