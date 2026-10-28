using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;

namespace Monetail.Api.Tests;

/// <summary>隔離DBとテスト専用鍵で、通常起動に必須の認証設定を用意する。</summary>
internal static class ApiTestSettings
{
    // テスト専用鍵の生成を一度にまとめ、ホストごとの設定・保存先は隔離する。
    private static readonly Lazy<byte[][]> Certificates = new(() => [CreateCertificate("signing"), CreateCertificate("encryption")]);

    /// <summary>同じ隔離フォルダの鍵を再利用し、CookieとOAuthの両方を構成する。</summary>
    public static IWebHostBuilder UseApiTestSettings(this IWebHostBuilder builder, AuthDatabaseFixture database)
    {
        var directory = Path.GetDirectoryName(database.DatabasePath)!;
        var names = new[] { "signing", "encryption" };
        for (var index = 0; index < names.Length; index++)
        {
            var path = Path.Combine(directory, names[index] + ".pfx");
            if (!File.Exists(path)) File.WriteAllBytes(path, Certificates.Value[index]);
        }
        return builder.UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Monetail", database.ConnectionString)
            .UseSetting("Passkeys:Origin", LoginTestApp.Origin).UseSetting("Passkeys:RpId", "monetail.example.test")
            .UseSetting("Passkeys:DataProtectionKeysPath", Path.Combine(directory, "keys"))
            .UseSetting("DesktopOAuth:SigningCertificatePath", Path.Combine(directory, "signing.pfx"))
            .UseSetting("DesktopOAuth:EncryptionCertificatePath", Path.Combine(directory, "encryption.pfx"));
    }

    /// <summary>実際の秘密情報を使わず、署名・暗号化で別々のRSA証明書を生成する。</summary>
    private static byte[] CreateCertificate(string name)
    {
        using var algorithm = RSA.Create(2048);
        var request = new CertificateRequest("CN=synthetic-" + name, algorithm, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(name == "signing"
            ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(2));
        return certificate.Export(X509ContentType.Pfx);
    }
}
