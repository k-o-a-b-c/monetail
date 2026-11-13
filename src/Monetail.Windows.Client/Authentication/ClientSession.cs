using Monetail.Windows.Client.Transport;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Authentication;

/// <summary>保存した認証をサーバーで確認し、必要な更新と失効時の削除を行う。</summary>
public sealed class ClientSession(MonetailApiClient api, ApiEndpoint endpoint, ITokenStore tokens,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private TokenSet? observedTokens;
    public TaxRateHistoryCache TaxRates { get; } = new();

    /// <summary>業務APIも期限を更新しながら呼び出す。拒否された時だけ認証を破棄する。</summary>
    public async Task<T> ExecuteAuthenticatedAsync<T>(Func<string, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = tokens.Load(endpoint);
            if (current is null) { TaxRates.Clear(); throw new AuthenticationRejectedException(); }
            EnsureSameContext(current);
            var refreshed = false;
            if (current.AccessTokenExpiresAtUtc <= time.GetUtcNow().AddSeconds(30))
            {
                current = await RefreshAsync(current, cancellationToken);
                refreshed = true;
            }
            try { return await operation(current.AccessToken, cancellationToken); }
            catch (AuthenticationRejectedException) when (!refreshed)
            {
                current = await RefreshAsync(current, cancellationToken);
                return await operation(current.AccessToken, cancellationToken);
            }
        }
        catch (AuthenticationRejectedException) { ClearObservedContext(); throw; }
        catch (AuthenticationContextChangedException) { TaxRates.Clear(); throw; }
        finally { gate.Release(); }
    }

    public async Task<AuthenticatedAccount?> RestoreAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            TaxRates.Clear();
            var restored = tokens.Load(endpoint);
            if (restored is null) return null;
            var validated = await ValidateAsync(restored, cancellationToken);
            observedTokens = validated?.Tokens;
            return validated?.Account;
        }
        finally { gate.Release(); }
    }

    public async Task<AuthenticatedAccount?> AcceptLoginAsync(TokenSet acquired,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            TaxRates.Clear();
            // 認可コードは再利用できないため、有効性確認中の通信障害でも取得済みトークンを残す。
            tokens.Save(endpoint, acquired);
            var validated = await ValidateAsync(acquired, cancellationToken);
            observedTokens = validated?.Tokens;
            return validated?.Account;
        }
        finally { gate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var restored = tokens.Load(endpoint);
            if (restored is null) { TaxRates.Clear(); return; }
            EnsureSameContext(restored);
            var validated = await ValidateAsync(restored, cancellationToken);
            if (validated is null) return;
            try { await api.LogoutAsync(validated.Value.Tokens.AccessToken, cancellationToken); }
            catch (AuthenticationRejectedException) { }
            // 通信障害ならここに到達しない。失効を確認できた時だけ端末側も消す。
            ClearObservedContext();
        }
        finally { gate.Release(); }
    }

    private async Task<(AuthenticatedAccount Account, TokenSet Tokens)?> ValidateAsync(TokenSet restored,
        CancellationToken cancellationToken)
    {
        observedTokens = restored;
        try
        {
            var current = restored;
            var refreshed = false;
            if (current.AccessTokenExpiresAtUtc <= time.GetUtcNow().AddSeconds(30))
            {
                current = await RefreshAsync(current, cancellationToken);
                refreshed = true;
            }
            AuthenticatedAccount account;
            try { account = await api.GetCurrentUserAsync(current.AccessToken, cancellationToken); }
            catch (AuthenticationRejectedException) when (!refreshed)
            {
                current = await RefreshAsync(current, cancellationToken);
                account = await api.GetCurrentUserAsync(current.AccessToken, cancellationToken);
            }
            return (account, current);
        }
        catch (AuthenticationRejectedException)
        {
            ClearObservedContext();
            return null;
        }
    }

    private async Task<TokenSet> RefreshAsync(TokenSet current, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(current.RefreshToken)) throw new AuthenticationRejectedException();
        var updated = await api.RefreshAsync(current.RefreshToken, cancellationToken);
        if (updated.RefreshToken is null)
            updated = new TokenSet(updated.AccessToken, current.RefreshToken, updated.AccessTokenExpiresAtUtc);
        // ローテーション後のトークンは、その後の確認が通信障害になっても失わない。
        EnsureSameContext(tokens.Load(endpoint) ?? throw new AuthenticationContextChangedException());
        tokens.Save(endpoint, updated);
        observedTokens = updated;
        return updated;
    }

    internal void EnsureCurrentContext()
    {
        var current = tokens.Load(endpoint);
        if (current is null) { TaxRates.Clear(); throw new AuthenticationContextChangedException(); }
        EnsureSameContext(current);
    }

    private void EnsureSameContext(TokenSet current)
    {
        if (observedTokens is not null &&
            (observedTokens.AccessToken != current.AccessToken || observedTokens.RefreshToken != current.RefreshToken ||
                observedTokens.AccessTokenExpiresAtUtc != current.AccessTokenExpiresAtUtc))
        {
            TaxRates.Clear();
            throw new AuthenticationContextChangedException();
        }
        observedTokens = current;
    }

    private void ClearObservedContext()
    {
        TaxRates.Clear();
        var current = tokens.Load(endpoint);
        if (current is null) return;
        EnsureSameContext(current);
        tokens.Clear();
        observedTokens = null;
    }
}
