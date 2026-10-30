using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Monetail.Windows.Client;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "monetail-client-tests", Guid.NewGuid().ToString("N"));
    private readonly ApiEndpoint endpoint = ApiEndpoint.Parse("https://api.example");
    private readonly TokenSet tokens = new("synthetic-access-token", "synthetic-refresh-token",
        new DateTimeOffset(2026, 10, 29, 2, 0, 0, TimeSpan.Zero));

    [Fact]
    public void SettingsContainOnlyVersionAndOriginAndReplacePreviousValue()
    {
        var settings = new ClientSettingsStore(directory);
        Assert.Null(settings.Load());
        settings.Save(endpoint);
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "settings.json"))))
        {
            Assert.Equal(2, json.RootElement.EnumerateObject().Count());
            Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(endpoint.Value, json.RootElement.GetProperty("apiUrl").GetString());
        }
        Assert.Equal(endpoint, settings.Load());
        var changed = ApiEndpoint.Parse("https://changed.example");
        settings.Save(changed);
        Assert.Equal(changed, settings.Load());
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("{bad-json}")]
    [InlineData("null")]
    [InlineData("{\"version\":2,\"apiUrl\":\"https://api.example\"}")]
    [InlineData("{\"version\":1,\"apiUrl\":\"http://api.example\"}")]
    public void InvalidSettingsRequireConnectionSetup(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), json);
        Assert.Null(new ClientSettingsStore(directory).Load());
    }

    [Fact]
    public void TokenRoundTripEncryptsWholePayloadAndPreservesExpiry()
    {
        using var protector = new TestProtector();
        var store = new FileTokenStore(directory, protector);
        Assert.Null(store.Load(endpoint));
        store.Save(endpoint, tokens);
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(TokenPath));
        Assert.DoesNotContain(tokens.AccessToken, raw);
        Assert.DoesNotContain(tokens.RefreshToken!, raw);
        Assert.DoesNotContain(endpoint.Value, raw);
        var restored = Assert.IsType<TokenSet>(store.Load(endpoint));
        Assert.Equal(tokens.AccessToken, restored.AccessToken);
        Assert.Equal(tokens.RefreshToken, restored.RefreshToken);
        Assert.Equal(tokens.AccessTokenExpiresAtUtc, restored.AccessTokenExpiresAtUtc);
        Assert.Single(Directory.GetFiles(directory));
        Assert.DoesNotContain(tokens.AccessToken, tokens.ToString());
        Assert.DoesNotContain(tokens.RefreshToken!, tokens.ToString());
    }

    [Fact]
    public void AnotherConnectionNeverGetsPreviousTokens()
    {
        using var protector = new TestProtector();
        var store = new FileTokenStore(directory, protector);
        store.Save(endpoint, tokens);
        Assert.Throws<Monetail.Windows.Client.Authentication.AuthenticationContextChangedException>(
            () => store.Load(ApiEndpoint.Parse("https://different.example")));
        Assert.True(File.Exists(TokenPath));
        Assert.Equal(tokens.AccessToken, store.Load(endpoint)!.AccessToken);
    }

    [Fact]
    public void DecryptionFailureDiscardsTokenFileAndPreservesSettings()
    {
        using var original = new TestProtector();
        using var changedUser = new TestProtector();
        new ClientSettingsStore(directory).Save(endpoint);
        new FileTokenStore(directory, original).Save(endpoint, tokens);
        Assert.Null(new FileTokenStore(directory, changedUser).Load(endpoint));
        Assert.False(File.Exists(TokenPath));
        Assert.Equal(endpoint, new ClientSettingsStore(directory).Load());
    }

    [Theory]
    [InlineData("{bad-json}")]
    [InlineData("null")]
    [InlineData("{\"Version\":2}")]
    [InlineData("{\"Version\":1,\"ApiUrl\":\"https://api.example/\",\"AccessToken\":\"\"}")]
    public void InvalidProtectedPayloadIsDiscarded(string invalidPayload)
    {
        using var protector = new TestProtector();
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(TokenPath, protector.Protect(Encoding.UTF8.GetBytes(invalidPayload)));
        Assert.Null(new FileTokenStore(directory, protector).Load(endpoint));
        Assert.False(File.Exists(TokenPath));
    }

    [Fact]
    public void FailedEncryptionKeepsExistingTokenFileAndDoesNotWritePlaintext()
    {
        using var protector = new TestProtector();
        var store = new FileTokenStore(directory, protector);
        store.Save(endpoint, tokens);
        var before = File.ReadAllBytes(TokenPath);
        Assert.Throws<CryptographicException>(() => new FileTokenStore(directory, new RejectingProtector()).Save(endpoint, tokens));
        Assert.Equal(before, File.ReadAllBytes(TokenPath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void IoFailureIsNotTreatedAsInvalidCredentials()
    {
        using var protector = new TestProtector();
        Directory.CreateDirectory(TokenPath);
        var error = Record.Exception(() => new FileTokenStore(directory, protector).Load(endpoint));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(TokenPath));
    }

    [Fact]
    public void ClearIsIdempotentAndPreservesConnectionSettings()
    {
        using var protector = new TestProtector();
        new ClientSettingsStore(directory).Save(endpoint);
        var store = new FileTokenStore(directory, protector);
        store.Save(endpoint, tokens);
        store.Clear();
        store.Clear();
        Assert.Null(store.Load(endpoint));
        Assert.Equal(endpoint, new ClientSettingsStore(directory).Load());
    }

    private string TokenPath => Path.Combine(directory, "authentication.dat");

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class RejectingProtector : IDataProtector
    {
        public byte[] Protect(byte[] plaintext) => throw new CryptographicException();
        public byte[] Unprotect(byte[] ciphertext) => throw new CryptographicException();
    }

    // テスト用の鍵をメモリーだけに置き、実際に暗号化して保存境界を検証する。
    private sealed class TestProtector : IDataProtector, IDisposable
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

        public byte[] Protect(byte[] plaintext)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var encrypted = new byte[plaintext.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, encrypted, tag);
            return [..nonce, ..tag, ..encrypted];
        }

        public byte[] Unprotect(byte[] ciphertext)
        {
            if (ciphertext.Length < 28) throw new CryptographicException();
            var plaintext = new byte[ciphertext.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(ciphertext.AsSpan(0, 12), ciphertext.AsSpan(28), ciphertext.AsSpan(12, 16), plaintext);
            return plaintext;
        }

        public void Dispose() => CryptographicOperations.ZeroMemory(key);
    }
}
