using System.Net;

namespace Monetail.Api.Authentication;

/// <summary>パスキー登録に使う固定オリジンと、信頼する配備設定を保持する。</summary>
public sealed class PasskeySettings
{
    public required Uri Origin { get; init; }
    public required string RpId { get; init; }
    public required string DataProtectionKeysPath { get; init; }
    public required IPAddress[] TrustedProxies { get; init; }

    /// <summary>外部設定を検証し、不完全な設定では登録画面を公開しない。</summary>
    public static PasskeySettings? Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Passkeys");
        if (!section.Exists()) return null;
        if (!Uri.TryCreate(section["Origin"], UriKind.Absolute, out var origin) ||
            origin.Scheme != "https" || origin.AbsolutePath != "/" ||
            origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0)
            throw new InvalidOperationException("Passkeys:Originに固定HTTPSオリジンを指定してください。");
        var rpId = section["RpId"];
        if (Uri.CheckHostName(rpId) != UriHostNameType.Dns ||
            !string.Equals(origin.IdnHost, rpId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Passkeys:RpIdにはOriginと同じホスト名を指定してください。");
        var keys = section["DataProtectionKeysPath"];
        if (string.IsNullOrWhiteSpace(keys) || !Path.IsPathFullyQualified(keys))
            throw new InvalidOperationException("Passkeys:DataProtectionKeysPathにキー保存先の絶対パスを指定してください。");
        var proxies = new List<IPAddress>();
        foreach (var entry in section.GetSection("TrustedProxies").GetChildren())
        {
            if (!IPAddress.TryParse(entry.Value, out var address))
                throw new InvalidOperationException("Passkeys:TrustedProxiesにはプロキシのIPアドレスを指定してください。");
            proxies.Add(address);
        }
        return new PasskeySettings
        {
            Origin = origin, RpId = rpId!.ToLowerInvariant(),
            DataProtectionKeysPath = keys, TrustedProxies = proxies.ToArray()
        };
    }

    /// <summary>HTTP要求が設定したHTTPSオリジンへの要求か判定する。</summary>
    public bool Matches(HttpRequest request) => request.IsHttps &&
        Uri.TryCreate($"https://{request.Host}", UriKind.Absolute, out var actual) &&
        string.Equals(actual.GetLeftPart(UriPartial.Authority), Origin.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);
}
