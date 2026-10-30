using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Monetail.Windows.Client.Authentication;

namespace Monetail.Windows.Client.Transport;

/// <summary>Cookieを共有せず、固定HTTPS接続先へWindows用の認証要求を送る。</summary>
public sealed partial class MonetailApiClient(ApiEndpoint endpoint, HttpClient http, TimeProvider? timeProvider = null)
{
    public const string ClientId = "monetail-windows";
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private const int MaximumResponseBytes = 65_536;
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectionTimeout);
        try { await CheckConnectionCoreAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ApiConnectionException(); }
    }

    private async Task CheckConnectionCoreAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, ".well-known/openid-configuration", null, null, false, cancellationToken);
        using var document = Parse(response);
        var metadata = document.RootElement;
        if (!SameEndpoint(metadata, "issuer", endpoint.BaseUri) ||
            !SameEndpoint(metadata, "authorization_endpoint", endpoint.Resolve("connect/authorize")) ||
            !SameEndpoint(metadata, "token_endpoint", endpoint.Resolve("connect/token")) ||
            !Contains(metadata, "response_types_supported", "code") ||
            !Contains(metadata, "code_challenge_methods_supported", "S256"))
            throw new ApiConnectionException();
    }

    public async Task<AuthenticatedAccount> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, "auth/me", accessToken, null, false, cancellationToken);
        using var document = Parse(response);
        var json = document.RootElement;
        if (!Guid.TryParse(String(json, "authUserId"), out var id) ||
            String(json, "displayName") is not { Length: > 0 } displayName ||
            !Date(json, "expiresAtUtc", out var expires) || !Date(json, "verifiedAtUtc", out var verified))
            throw new ApiConnectionException();
        return new AuthenticatedAccount(id, displayName, expires, verified);
    }

    public Task<TokenSet> ExchangeCodeAsync(string code, string verifier, Uri redirectUri,
        CancellationToken cancellationToken = default) => ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri.AbsoluteUri, ["client_id"] = ClientId
        }, cancellationToken);

    public Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = ClientId
        }, cancellationToken);

    public async Task LogoutAsync(string accessToken, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Post, "auth/logout", accessToken, null, false, cancellationToken);

    private async Task<TokenSet> ExchangeAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        // 応答到着時刻を起点にしてサーバーの期限を過大評価しないよう、送信前に記録する。
        var requestedAt = time.GetUtcNow();
        var response = await SendAsync(HttpMethod.Post, "connect/token", null, form, true, cancellationToken);
        using var document = Parse(response);
        var json = document.RootElement;
        if (!string.Equals(String(json, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase) ||
            String(json, "access_token") is not { Length: > 0 } accessToken ||
            !json.TryGetProperty("expires_in", out var lifetime) || lifetime.ValueKind != JsonValueKind.Number || !lifetime.TryGetInt32(out var seconds) || seconds <= 0 || seconds > 900)
            throw new ApiConnectionException();
        var refresh = String(json, "refresh_token");
        return new TokenSet(accessToken, string.IsNullOrEmpty(refresh) ? null : refresh, requestedAt.AddSeconds(seconds));
    }

    private async Task<byte[]> SendAsync(HttpMethod method, string path, string? bearer,
        Dictionary<string, string>? form, bool tokenExchange, CancellationToken cancellationToken, HttpContent? body = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectionTimeout);
        try
        {
            using var request = new HttpRequestMessage(method, endpoint.Resolve(path));
            if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (form is not null) request.Content = new FormUrlEncodedContent(form);
            else if (body is not null) request.Content = body;
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized && bearer is not null) throw new AuthenticationRejectedException();
            // 方向転換先へ認証情報を持ち出さない。生成用HttpClientでは自動転送も無効にする。
            if ((int)response.StatusCode is >= 300 and < 400) throw new ApiConnectionException();
            var bytes = await ReadBoundedAsync(response.Content, timeout.Token);
            if (path.StartsWith("ledgers", StringComparison.Ordinal) && response.StatusCode is
                HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests)
                throw new LedgerRequestException(response.StatusCode);
            if (tokenExchange && response.StatusCode == HttpStatusCode.BadRequest)
            {
                using var error = Parse(bytes);
                if (String(error.RootElement, "error") == "invalid_grant") throw new AuthenticationRejectedException();
            }
            if (!response.IsSuccessStatusCode) throw new ApiConnectionException();
            return bytes;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ApiConnectionException(); }
        catch (HttpRequestException) { throw new ApiConnectionException(); }
        catch (IOException) { throw new ApiConnectionException(); }
        catch (FormatException) { throw new ApiConnectionException(); }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes) throw new ApiConnectionException();
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (destination.Length + count > MaximumResponseBytes) throw new ApiConnectionException();
            destination.Write(buffer, 0, count);
        }
        return destination.ToArray();
    }

    private static JsonDocument Parse(byte[] bytes)
    {
        try
        {
            var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException) { }
        throw new ApiConnectionException();
    }

    private static string? String(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Date(JsonElement json, string name, out DateTimeOffset value)
    {
        value = default;
        return json.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && item.TryGetDateTimeOffset(out value);
    }

    private static bool SameEndpoint(JsonElement json, string name, Uri expected) =>
        Uri.TryCreate(String(json, name), UriKind.Absolute, out var value) &&
        value.UserInfo.Length == 0 && value.Fragment.Length == 0 && value == expected;

    private static bool Contains(JsonElement json, string name, string expected) =>
        json.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array &&
        values.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == expected);
}
