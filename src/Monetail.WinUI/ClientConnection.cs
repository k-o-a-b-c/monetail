using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Windows.System;

namespace Monetail.WinUI;

/// <summary>画面が使う既存Clientを構成する。トークンを表示用データへ渡さない。</summary>
internal sealed class ClientConnection(ClientSettingsStore settings, ITokenStore tokens, HttpClient http,
    Func<Uri, Task<bool>> openBrowser) : IDisposable
{
    public LedgerMasterCache Masters { get; } = new();
    public ApiEndpoint? Endpoint { get; private set; }
    public MonetailApiClient Api { get; private set; } = null!;
    public ClientSession Session { get; private set; } = null!;
    public LedgerWorkspace Ledgers { get; private set; } = null!;
    public Func<Uri, Task<bool>> OpenBrowser { get; } = openBrowser;

    public static ClientConnection CreateDefault()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monetail");
        return new(new ClientSettingsStore(directory), new FileTokenStore(directory, new DpapiDataProtector()),
            MonetailApiClient.CreateHttpClient(), async uri => await Launcher.LaunchUriAsync(uri));
    }

    public bool LoadSettings()
    {
        var saved = settings.Load();
        if (saved is null) return false;
        SetEndpoint(saved);
        return true;
    }

    public async Task ConnectAsync(ApiEndpoint candidate, CancellationToken token)
    {
        var candidateApi = new MonetailApiClient(candidate, http);
        await candidateApi.CheckConnectionAsync(token);
        // 接続を確認するまでは設定・認証を変更しない。変更先へ元のトークンを送らない。
        settings.Save(candidate);
        if (!Equals(Endpoint, candidate)) tokens.Clear();
        SetEndpoint(candidate);
    }

    private void SetEndpoint(ApiEndpoint endpoint)
    {
        Masters.Clear(); Session?.TaxRates.Clear();
        Endpoint = endpoint;
        Api = new(endpoint, http);
        Session = new(Api, endpoint, tokens);
        Ledgers = new(Api, Session);
    }

    public void Dispose() { Masters.Clear(); Session?.TaxRates.Clear(); http.Dispose(); }
}
