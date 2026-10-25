using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monetail.Api.Authentication;

/// <summary>実行時・移行生成時・テストで同じ認証DB構成を登録する。</summary>
public static class AuthDatabaseServices
{
    /// <summary>既存ファイルへの接続とパスキー対応のIdentityストアを登録する。</summary>
    public static IServiceCollection AddAuthDatabase(this IServiceCollection services, string connectionString)
    {
        var connection = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connection.DataSource) || connection.DataSource == ":memory:")
            throw new ArgumentException("認証DBには既存のSQLiteファイルを指定してください。", nameof(connectionString));
        connection.Mode = SqliteOpenMode.ReadWrite;
        connection.ForeignKeys = true;

        services.AddLogging();
        services.AddIdentityCore<AuthUser>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        }).AddRoles<IdentityRole>().AddEntityFrameworkStores<AuthDbContext>();
        services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection.ToString()));
        services.AddScoped<AuthDatabaseMigrator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AdminProvisioningService>();
        services.AddScoped<AuthenticationRevocationService>();
        return services;
    }
}
