using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Authentication;

/// <summary>保存した認証をサーバーで確認し、必要な更新と失効時の削除を行う。</summary>
public sealed class ClientSession(MonetailApiClient api, ApiEndpoint endpoint, ITokenStore tokens,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<AuthenticatedAccount?> RestoreAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var restored = tokens.Load(endpoint);
            if (restored is null) return null;
            var validated = await ValidateAsync(restored, cancellationToken);
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
            // 認可コードは再利用できないため、有効性確認中の通信障害でも取得済みトークンを残す。
            tokens.Save(endpoint, acquired);
            var validated = await ValidateAsync(acquired, cancellationToken);
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
            if (restored is null) return;
            var validated = await ValidateAsync(restored, cancellationToken);
            if (validated is null) return;
            try { await api.LogoutAsync(validated.Value.Tokens.AccessToken, cancellationToken); }
            catch (AuthenticationRejectedException) { }
            // 通信障害ならここに到達しない。失効を確認できた時だけ端末側も消す。
            tokens.Clear();
        }
        finally { gate.Release(); }
    }

    private async Task<(AuthenticatedAccount Account, TokenSet Tokens)?> ValidateAsync(TokenSet restored,
        CancellationToken cancellationToken)
    {
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
            tokens.Clear();
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
        tokens.Save(endpoint, updated);
        return updated;
    }
}
