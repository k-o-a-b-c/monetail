using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Authentication;

/// <summary>外部ブラウザーと、実行中だけ開くIPv4ループバックで認可コードを受け取る。</summary>
public sealed class BrowserOAuthLogin : IAsyncDisposable
{
    private readonly MonetailApiClient api;
    private readonly TcpListener listener;
    private readonly string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
    private readonly string state = Base64Url(RandomNumberGenerator.GetBytes(32));
    private int started;
    private bool disposed;

    public Uri AuthorizationUri { get; }
    public Uri RedirectUri { get; }

    public BrowserOAuthLogin(MonetailApiClient api, ApiEndpoint endpoint)
    {
        this.api = api;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start();
        RedirectUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/callback/");
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["client_id"] = MonetailApiClient.ClientId, ["response_type"] = "code", ["response_mode"] = "query",
            ["redirect_uri"] = RedirectUri.AbsoluteUri, ["scope"] = "monetail-api offline_access",
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state
        };
        AuthorizationUri = new Uri(endpoint.Resolve("connect/authorize").AbsoluteUri + "?" +
            string.Join("&", query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
    }

    public async Task<TokenSet> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("認証待ちは一度だけ開始できます。");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            var stream = client.GetStream();
            Dictionary<string, string>? query;
            try
            {
                query = await ReadCallbackAsync(stream, requestTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { continue; }
            catch (IOException) { continue; }
            if (query is null || !query.TryGetValue("state", out var receivedState) || !StateMatches(receivedState) ||
                query.ContainsKey("code") == query.ContainsKey("error") ||
                (query.TryGetValue("code", out var suppliedCode) && string.IsNullOrWhiteSpace(suppliedCode)))
            {
                await ReplySafelyAsync(stream, false, false, cancellationToken);
                continue;
            }
            if (query.ContainsKey("error"))
            {
                await ReplySafelyAsync(stream, false, true, cancellationToken);
                throw new AuthenticationRejectedException();
            }
            try
            {
                var tokens = await api.ExchangeCodeAsync(query["code"], verifier, RedirectUri, cancellationToken);
                await ReplySafelyAsync(stream, true, true, cancellationToken);
                return tokens;
            }
            catch
            {
                await ReplySafelyAsync(stream, false, true, cancellationToken);
                throw;
            }
        }
    }

    private bool StateMatches(string supplied) => supplied.Length == state.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(state));

    private async Task<Dictionary<string, string>?> ReadCallbackAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // HTTPの受付はこの固定パスへのGETだけ。本文・転送・外部インターフェースは扱わない。
        var bytes = new List<byte>();
        var next = new byte[1];
        while (bytes.Count < 16_384)
        {
            if (await stream.ReadAsync(next, cancellationToken) == 0) return null;
            bytes.Add(next[0]);
            if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10) break;
        }
        if (bytes.Count >= 16_384) return null;
        var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3 || first[0] != "GET" || first[2] is not ("HTTP/1.1" or "HTTP/1.0") ||
            !first[1].StartsWith("/callback/?", StringComparison.Ordinal) || first[1].Contains('#')) return null;
        var hosts = lines.Skip(1).Where(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (hosts.Length != 1 || !string.Equals(hosts[0][5..].Trim(), RedirectUri.Authority, StringComparison.OrdinalIgnoreCase)) return null;
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var part in first[1][("/callback/?".Length)..].Split('&'))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2 || !ValidEscapes(pair[0]) || !ValidEscapes(pair[1]) ||
                    !query.TryAdd(WebUtility.UrlDecode(pair[0]), WebUtility.UrlDecode(pair[1]))) return null;
            }
        }
        catch (ArgumentException) { return null; }
        return query;
    }

    private static bool ValidEscapes(string text)
    {
        for (var index = 0; index < text.Length; index++)
            if (text[index] == '%' && (index + 2 >= text.Length || !Uri.IsHexDigit(text[++index]) || !Uri.IsHexDigit(text[++index]))) return false;
        return true;
    }

    private static async Task ReplySafelyAsync(NetworkStream stream, bool success, bool accepted, CancellationToken cancellationToken)
    {
        const string script = "window.close();";
        var message = success ? "認証が完了しました。このタブを閉じて、Monetailに戻ってください。" :
            accepted ? "認証を完了できませんでした。Monetailに戻って、もう一度ログインしてください。" : "この認証要求を受け付けられません。";
        var body = Encoding.UTF8.GetBytes("<!doctype html><html lang=\"ja\"><meta charset=\"utf-8\"><title>Monetail</title>" +
            "<p>" + message + "</p>" + (success ? "<script>" + script + "</script>" : "") + "</html>");
        var scriptHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {(accepted ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'none'; script-src 'sha256-{scriptHash}'; base-uri 'none'; frame-ancestors 'none'\r\n\r\n");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await stream.WriteAsync(headers, timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
            await stream.FlushAsync(timeout.Token);
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        // タブ側の切断や自動終了の可否は認証成功に影響させない。
    }

    public ValueTask DisposeAsync()
    {
        disposed = true;
        listener.Stop();
        return ValueTask.CompletedTask;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
