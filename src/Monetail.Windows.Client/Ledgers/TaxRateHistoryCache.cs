using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

/// <summary>接続先/認証セッション内で共有する全履歴。期限・ディスク保存・暗黙の再取得はない。</summary>
public sealed class TaxRateHistoryCache
{
    private readonly object gate = new();
    private long generation;
    private TaxRatesInfo? value;
    private Task<TaxRatesInfo>? pending;
    public TaxRatesInfo? Current { get { lock (gate) return value; } }
    public void Clear() { lock (gate) { generation++; value = null; pending = null; } }
    public Task<TaxRatesInfo> GetAsync(Func<CancellationToken, Task<TaxRatesInfo>> load, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); long epoch; TaskCompletionSource<TaxRatesInfo> completion;
        lock (gate)
        {
            if (value is not null) return Task.FromResult(value);
            if (pending is not null) return pending.WaitAsync(token);
            epoch = generation; completion = new(TaskCreationOptions.RunContinuationsAsynchronously); pending = completion.Task;
        }
        _ = Load(); return completion.Task;
        async Task Load()
        {
            try
            {
                var loaded = await load(token);
                lock (gate)
                {
                    if (generation != epoch) throw new AuthenticationContextChangedException();
                    value = loaded; pending = null;
                }
                completion.TrySetResult(loaded);
            }
            catch (OperationCanceledException) { Reset(); completion.TrySetCanceled(token); }
            catch (Exception error) { Reset(); completion.TrySetException(error); }
            void Reset() { lock (gate) if (generation == epoch) pending = null; }
        }
    }
}

public sealed class TaxRatesWorkspace(MonetailApiClient api, ClientSession session, Guid ledgerId)
{
    // キャッシュヒットでも現在のログインコンテキストを検証。トークン更新では履歴を破棄しない。
    public Task<TaxRatesInfo> ListAsync(CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync(async (access, ct) =>
        {
            var result = await session.TaxRates.GetAsync(t => api.ListTaxRatesAsync(access, ledgerId, t), ct);
            session.EnsureCurrentContext();
            return result;
        }, token);
}
