using System.Security.Cryptography;
using System.Text;
using Monetail.Windows.Client;
using Xunit;

namespace Monetail.Windows.Tests;

public sealed class DpapiStorageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "monetail-dpapi-tests", Guid.NewGuid().ToString("N"));
    private readonly ApiEndpoint endpoint = ApiEndpoint.Parse("https://synthetic-api.example");

    [Fact]
    public void CurrentUserDpapiPersistsEncryptedTokensAndRestoresWithNewStoreInstance()
    {
        var tokens = new TokenSet("synthetic-dpapi-access-token", "synthetic-dpapi-refresh-token",
            new DateTimeOffset(2026, 10, 29, 2, 0, 0, TimeSpan.Zero));
        new ClientSettingsStore(directory).Save(endpoint);
        new FileTokenStore(directory, new DpapiDataProtector()).Save(endpoint, tokens);

        var encrypted = File.ReadAllBytes(Path.Combine(directory, "authentication.dat"));
        var raw = Encoding.UTF8.GetString(encrypted);
        Assert.DoesNotContain(tokens.AccessToken, raw);
        Assert.DoesNotContain(tokens.RefreshToken!, raw);
        Assert.DoesNotContain(endpoint.Value, raw);
        var restored = Assert.IsType<TokenSet>(new FileTokenStore(directory, new DpapiDataProtector()).Load(endpoint));
        Assert.Equal(tokens.AccessToken, restored.AccessToken);
        Assert.Equal(tokens.RefreshToken, restored.RefreshToken);
        Assert.Equal(tokens.AccessTokenExpiresAtUtc, restored.AccessTokenExpiresAtUtc);
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public void CorruptDpapiFileRequiresLoginAndKeepsConnectionSettings()
    {
        var tokens = new TokenSet("synthetic-dpapi-access-token", null, DateTimeOffset.UtcNow.AddMinutes(10));
        new ClientSettingsStore(directory).Save(endpoint);
        new FileTokenStore(directory, new DpapiDataProtector()).Save(endpoint, tokens);
        var path = Path.Combine(directory, "authentication.dat");
        var encrypted = File.ReadAllBytes(path);
        encrypted[^1] ^= 0xff;
        File.WriteAllBytes(path, encrypted);

        Assert.Null(new FileTokenStore(directory, new DpapiDataProtector()).Load(endpoint));
        Assert.False(File.Exists(path));
        Assert.Equal(endpoint, new ClientSettingsStore(directory).Load());
    }

    [Fact]
    public void DpapiDoesNotAcceptArbitraryPlaintextAsProtectedData()
    {
        Assert.Throws<CryptographicException>(() =>
            new DpapiDataProtector().Unprotect(Encoding.UTF8.GetBytes("synthetic-plaintext-token")));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
