using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monetail.Api.Authentication.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCreationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_account_creation_requests",
                columns: table => new
                {
                    RequestId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByAuthUserId = table.Column<string>(type: "TEXT", nullable: false),
                    AuthUserId = table.Column<string>(type: "TEXT", nullable: false),
                    UserCode = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_account_creation_requests", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_auth_account_creation_requests_AspNetUsers_AuthUserId",
                        column: x => x.AuthUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_auth_account_creation_requests_AspNetUsers_CreatedByAuthUserId",
                        column: x => x.CreatedByAuthUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_auth_account_creation_requests_m_user_UserCode",
                        column: x => x.UserCode,
                        principalTable: "m_user",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_account_creation_requests_AuthUserId",
                table: "auth_account_creation_requests",
                column: "AuthUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_account_creation_requests_CreatedByAuthUserId",
                table: "auth_account_creation_requests",
                column: "CreatedByAuthUserId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_account_creation_requests_UserCode",
                table: "auth_account_creation_requests",
                column: "UserCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_account_creation_requests");
        }
    }
}
