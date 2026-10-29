namespace Monetail.Windows.Client;

// recordの自動ToStringでトークンを表示しない。
public sealed class TokenSet(string accessToken, string? refreshToken, DateTimeOffset accessTokenExpiresAtUtc)
{
    public string AccessToken { get; } = !string.IsNullOrWhiteSpace(accessToken)
        ? accessToken : throw new ArgumentException("アクセストークンが必要です。", nameof(accessToken));
    public string? RefreshToken { get; } = refreshToken;
    public DateTimeOffset AccessTokenExpiresAtUtc { get; } = accessTokenExpiresAtUtc;
    public override string ToString() => nameof(TokenSet);
}
