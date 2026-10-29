using System.Security.Cryptography;
using System.Text.Json;

namespace Monetail.Windows.Client;

public sealed class FileTokenStore(string directory, IDataProtector protector) : ITokenStore
{
    private readonly string path = Path.Combine(directory, "authentication.dat");

    public TokenSet? Load(ApiEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        byte[] encrypted;
        try { encrypted = File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        byte[]? plaintext = null;
        try
        {
            plaintext = protector.Unprotect(encrypted);
            var payload = JsonSerializer.Deserialize<Payload>(plaintext);
            if (payload is not { Version: 1 } || payload.ApiUrl != endpoint.Value ||
                string.IsNullOrWhiteSpace(payload.AccessToken) ||
                payload.AccessTokenExpiresAtUtc == default)
                return Discard();
            return new TokenSet(payload.AccessToken, payload.RefreshToken, payload.AccessTokenExpiresAtUtc);
        }
        catch (CryptographicException) { return Discard(); }
        catch (JsonException) { return Discard(); }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Save(ApiEndpoint endpoint, TokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(tokens);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new Payload(1, endpoint.Value,
            tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresAtUtc));
        try
        {
            // ディスクへ渡すのは暗号化後のデータだけ。一時ファイルも平文にしない。
            var encrypted = protector.Protect(plaintext);
            AtomicFile.Write(path, encrypted);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Clear()
    {
        try { File.Delete(path); }
        catch (DirectoryNotFoundException) { }
    }

    private TokenSet? Discard()
    {
        Clear();
        return null;
    }

    private sealed class Payload(int version, string apiUrl, string accessToken,
        string? refreshToken, DateTimeOffset accessTokenExpiresAtUtc)
    {
        public int Version { get; } = version;
        public string ApiUrl { get; } = apiUrl;
        public string AccessToken { get; } = accessToken;
        public string? RefreshToken { get; } = refreshToken;
        public DateTimeOffset AccessTokenExpiresAtUtc { get; } = accessTokenExpiresAtUtc;
    }
}
