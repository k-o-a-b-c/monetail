using System.Net;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

/// <summary>起動時の家計簿または一覧・作成画面への行き先を保持する。</summary>
public sealed record LedgerDestination(LedgerInfo? Opened, IReadOnlyList<LedgerInfo> Choices);

/// <summary>通信失敗時の再送でも同じ要求IDと名前を使う作成操作。</summary>
public sealed record LedgerCreationDraft
{
    public Guid RequestId { get; } = Guid.NewGuid();
    public string Name { get; }
    public LedgerCreationDraft(string name)
    {
        Name = name.Trim();
        if (Name.Length is < 1 or > 100) throw new FormatException("家計簿名は1〜100文字で入力してください。");
    }
}

/// <summary>トークンを画面へ渡さず、現在の参加を確認して家計簿操作を行う。</summary>
public sealed class LedgerWorkspace(MonetailApiClient api, ClientSession session)
{
    public async Task<LedgerDestination> StartAsync(CancellationToken token = default)
    {
        var previous = await session.ExecuteAuthenticatedAsync(api.OpenLastLedgerAsync, token);
        if (previous is not null) return new(previous, []);
        var choices = await ListAsync(token);
        if (choices.Length == 1)
        {
            try { return new(await OpenAsync(choices[0].LedgerId, token), []); }
            catch (LedgerRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
            {
                choices = await ListAsync(token);
            }
        }
        return new(null, choices);
    }

    public Task<LedgerInfo[]> ListAsync(CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync(api.ListLedgersAsync, token);

    public Task<LedgerInfo> OpenAsync(Guid ledgerId, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.OpenLedgerAsync(access, ledgerId, ct), token);

    public Task<LedgerInfo> CreateAsync(LedgerCreationDraft draft, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.CreateLedgerAsync(access, draft.Name, draft.RequestId, ct), token);

}
