using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckTaxRateConnectionCache()
    {
        using var fixture = new SyntheticConnection(saved: true);
        using var connection = fixture.CreateConnection();
        Check(connection.LoadSettings(), "税率キャッシュ検証は既存の接続設定を使用");
        var previous = connection.Session; var value = new TaxRatesInfo([new("Standard", DateOnly.MinValue, null, 0)]);
        await previous.TaxRates.GetAsync(_ => Task.FromResult(value));
        await connection.ConnectAsync(ApiEndpoint.Parse("https://tax-cache-other.example.test"), CancellationToken.None);
        Check(previous.TaxRates.Current is null && connection.Session.TaxRates.Current is null && !ReferenceEquals(previous, connection.Session),
            "接続先変更で元のセッションの税率キャッシュを破棄し、新しいキャッシュを開始");
        Check(fixture.OtherServerRequests.SequenceEqual(["/.well-known/openid-configuration"]), "税率キャッシュの切替で元の認証を変更先へ送らない");
        var pending = new TaskCompletionSource<TaxRatesInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = connection.Session.TaxRates.GetAsync(_ => pending.Task); var cache = connection.Session.TaxRates;
        connection.Dispose(); pending.SetResult(value);
        try { await old; throw new InvalidOperationException("破棄後の税率応答が採用されました。"); }
        catch (AuthenticationContextChangedException) { }
        Check(cache.Current is null, "接続破棄後に届いた税率履歴を拒否しキャッシュへ保存しない");
    }
}
