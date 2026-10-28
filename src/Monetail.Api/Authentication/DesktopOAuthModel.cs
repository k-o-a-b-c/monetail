using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Monetail.Api.Authentication;

/// <summary>OAuthの標準保存モデルと、公式Windowsクライアントの公開設定を定義する。</summary>
public static class DesktopOAuthModel
{
    /// <summary>利用者IDとは別の、公式Windowsアプリの公開識別子を定める。</summary>
    public const string ClientId = "monetail-windows";
    /// <summary>Monetail APIを呼ぶためのOAuthスコープを定める。</summary>
    public const string Scope = "monetail-api";
    /// <summary>Windowsアプリのループバック復帰先を定める。</summary>
    public const string Callback = "http://127.0.0.1/callback/";

    /// <summary>空DBの初回定義に、秘密情報を持たないWindowsクライアントを登録する。</summary>
    public static void Configure(ModelBuilder builder)
    {
        builder.UseOpenIddict();
        builder.Entity<OpenIddictEntityFrameworkCoreApplication>().HasData(new OpenIddictEntityFrameworkCoreApplication
        {
            Id = "monetail-windows", ConcurrencyToken = "monetail-windows-initial",
            ClientId = ClientId, DisplayName = "Monetail Windows",
            ApplicationType = ApplicationTypes.Native, ClientType = ClientTypes.Public, ConsentType = ConsentTypes.Implicit,
            RedirectUris = JsonSerializer.Serialize(new[] { Callback }),
            Permissions = JsonSerializer.Serialize(new[] {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + Scope }),
            Requirements = JsonSerializer.Serialize(new[] { Requirements.Features.ProofKeyForCodeExchange })
        });
    }
}
