using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Records;

// 入力値を持たず、入力画面が利用する既存Clientと認証コンテキストだけを束ねる。
public sealed class RecordEditorContext(MonetailApiClient api, ClientSession session, LedgerMasterCache masters,
    Guid ledgerId, Guid accountId, string origin, CancellationToken windowToken)
{
    public MonetailApiClient Api { get; private set; } = api;
    public ClientSession Session { get; private set; } = session;
    public LedgerMasterCache Masters { get; } = masters;
    public Guid LedgerId { get; } = ledgerId;
    public Guid AccountId { get; } = accountId;
    public string Origin { get; } = origin;
    // 書込にPageの読取取消を流用しない。
    public CancellationToken WindowToken { get; } = windowToken;
    public void Rebind(MonetailApiClient currentApi, ClientSession currentSession)
    {
        Api = currentApi;
        Session = currentSession;
    }
}
