using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monetail.Api.Authentication.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_enrollment_codes",
                columns: table => new
                {
                    CodeHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    AuthUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_enrollment_codes", x => x.CodeHash);
                    table.ForeignKey(
                        name: "FK_auth_enrollment_codes_AspNetUsers_AuthUserId",
                        column: x => x.AuthUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_enrollment_codes_AuthUserId",
                table: "auth_enrollment_codes",
                column: "AuthUserId",
                unique: true,
                filter: "\"ConsumedAtUtc\" IS NULL AND \"RevokedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_enrollment_codes");
        }
    }
}
