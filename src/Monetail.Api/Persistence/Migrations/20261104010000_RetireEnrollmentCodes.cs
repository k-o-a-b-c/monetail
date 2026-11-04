using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monetail.Api.Persistence.Migrations;

/// <summary>旧方式の未使用トークンだけを失効し、登録済みアカウント・鍵・履歴は保持する。</summary>
[DbContext(typeof(ApiDbContext))]
[Migration("20261104010000_RetireEnrollmentCodes")]
public sealed class RetireEnrollmentCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        "UPDATE auth_enrollment_codes SET RevokedAtUtc=CURRENT_TIMESTAMP WHERE Purpose='passkey-enrollment' AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL");

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 失効した秘密を再び有効にはしない。既存の鍵・アカウントには変更を加えない。
    }
}
