using System.Security.Cryptography.X509Certificates;

namespace Monetail.Api.Authentication;

/// <summary>通常起動に必須のOAuth署名・暗号化証明書を指定する。</summary>
public sealed class DesktopOAuthSettings
{
    /// <summary>永続化した署名用PFXの絶対パスを保持する。</summary>
    public required string SigningCertificatePath { get; init; }
    /// <summary>永続化した暗号化用PFXの絶対パスを保持する。</summary>
    public required string EncryptionCertificatePath { get; init; }
    /// <summary>署名用PFXの読込パスワードを保持する。</summary>
    public string? SigningCertificatePassword { get; init; }
    /// <summary>暗号化用PFXの読込パスワードを保持する。</summary>
    public string? EncryptionCertificatePassword { get; init; }

    /// <summary>必須の署名・暗号化証明書の絶対パスを検証する。</summary>
    public static DesktopOAuthSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("DesktopOAuth");
        var signing = section["SigningCertificatePath"];
        var encryption = section["EncryptionCertificatePath"];
        if (string.IsNullOrWhiteSpace(signing) || !Path.IsPathFullyQualified(signing) ||
            string.IsNullOrWhiteSpace(encryption) || !Path.IsPathFullyQualified(encryption) || signing == encryption)
            throw new InvalidOperationException("DesktopOAuthに別々の署名・暗号化証明書の絶対パスを指定してください。");
        return new()
        {
            SigningCertificatePath = signing, EncryptionCertificatePath = encryption,
            SigningCertificatePassword = section["SigningCertificatePassword"],
            EncryptionCertificatePassword = section["EncryptionCertificatePassword"]
        };
    }
}

/// <summary>OAuthの署名・暗号化鍵を読み込み、プロセス終了時に解放する。</summary>
public sealed class DesktopOAuthCredentials : IDisposable
{
    /// <summary>OAuthのコード・トークンを署名する秘密鍵付き証明書を保持する。</summary>
    public X509Certificate2 Signing { get; }
    /// <summary>コード・トークンを暗号化する秘密鍵付き証明書を保持する。</summary>
    public X509Certificate2 Encryption { get; }

    /// <summary>別々の秘密鍵付きRSA証明書を、OSの証明書ストアへ保存せず読み込む。</summary>
    public DesktopOAuthCredentials(DesktopOAuthSettings settings, TimeProvider time)
    {
        Signing = X509CertificateLoader.LoadPkcs12FromFile(settings.SigningCertificatePath,
            settings.SigningCertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
        try
        {
            Encryption = X509CertificateLoader.LoadPkcs12FromFile(settings.EncryptionCertificatePath,
                settings.EncryptionCertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            using var signingKey = Signing.GetRSAPrivateKey();
            using var encryptionKey = Encryption.GetRSAPrivateKey();
            var now = time.GetUtcNow().UtcDateTime;
            if (signingKey is null || encryptionKey is null || signingKey.KeySize < 2048 || encryptionKey.KeySize < 2048 ||
                signingKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(encryptionKey.ExportSubjectPublicKeyInfo()) ||
                Signing.NotBefore.ToUniversalTime() > now ||
                Signing.NotAfter.ToUniversalTime() <= now || Encryption.NotBefore.ToUniversalTime() > now ||
                Encryption.NotAfter.ToUniversalTime() <= now)
                throw new InvalidOperationException("OAuthには有効期限内の別々のRSA秘密鍵付き証明書（2048ビット以上）が必要です。");
        }
        catch { Signing.Dispose(); Encryption?.Dispose(); throw; }
    }

    /// <summary>読み込んだ秘密鍵と証明書を解放する。</summary>
    public void Dispose() { Signing.Dispose(); Encryption.Dispose(); }
}
