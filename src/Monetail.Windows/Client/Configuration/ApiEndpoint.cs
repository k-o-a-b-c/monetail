namespace Monetail.Windows.Client;

/// <summary>認証情報を送信する、固定HTTPSオリジン。</summary>
public sealed class ApiEndpoint : IEquatable<ApiEndpoint>
{
    private ApiEndpoint(Uri baseUri) => BaseUri = baseUri;

    public Uri BaseUri { get; }
    public string Value => BaseUri.AbsoluteUri;

    public static ApiEndpoint Parse(string value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsControl) ||
            text.IndexOfAny(['?', '#', '\\', '@']) >= 0 ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !uri.IsWellFormedOriginalString() || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
            HasOriginalPath(text))
            throw new FormatException("接続先には、パス・認証情報を含まないHTTPSのサーバーURLを入力してください。");

        var normalized = new UriBuilder(Uri.UriSchemeHttps, uri.IdnHost.ToLowerInvariant(),
            uri.IsDefaultPort ? -1 : uri.Port).Uri;
        return new ApiEndpoint(normalized);
    }

    private static bool HasOriginalPath(string text)
    {
        var pathStart = text.IndexOf('/', text.IndexOf("://", StringComparison.Ordinal) + 3);
        return pathStart >= 0 && pathStart != text.Length - 1;
    }

    public Uri Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var uri = new Uri(BaseUri, relativePath);
        if (uri.Scheme != BaseUri.Scheme || uri.Authority != BaseUri.Authority ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("接続先とは異なるオリジンを指定できません。", nameof(relativePath));
        return uri;
    }

    public bool Equals(ApiEndpoint? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is ApiEndpoint other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;
}
