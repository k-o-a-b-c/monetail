using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;

namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>家計簿名と現在の自分の役割・更新版を返す。</summary>
public sealed record LedgerResponse(string LedgerId, string Name, string Role, string Revision);

/// <summary>家計簿を開く操作の結果と、公開できるデータを保持する。</summary>
public enum LedgerOpenStatus { Opened, NotFound, AuthenticationChanged }
public sealed record LedgerOpenResult(LedgerOpenStatus Status, LedgerResponse? Response = null);

/// <summary>家計簿作成の成功・再送・競合・認証失効を区別する。</summary>
public enum LedgerCreationStatus { Created, Replayed, Conflict, NotFound, AuthenticationChanged }

/// <summary>作成結果と、公開できる場合だけ家計簿情報を保持する。</summary>
public sealed record LedgerCreationResult(LedgerCreationStatus Status, LedgerResponse? Response = null);

/// <summary>家計簿を原子的に作成し、現在の参加範囲だけを参照する。</summary>
public sealed class LedgerService(ApiDbContext context, TimeProvider time, ILogger<LedgerService> logger)
{
    /// <summary>履歴は権限として使わず、現在も参加している場合だけ返す。参照では履歴を書き換えない。</summary>
    public Task<LedgerResponse?> LastOpenedAsync(CurrentSession current, CancellationToken token) =>
        (from navigation in context.Set<LedgerNavigation>().AsNoTracking()
         join ledger in context.Set<Ledger>().AsNoTracking() on navigation.LastOpenedLedgerId equals ledger.Id
         join member in context.Set<LedgerMember>().AsNoTracking() on ledger.Id equals member.LedgerId
         where navigation.AuthUserId == current.Session.AuthUserId && member.AuthUserId == current.Session.AuthUserId
         select new LedgerResponse(ledger.Id, ledger.Name, member.Role, ledger.Revision)).SingleOrDefaultAsync(token);

    /// <summary>書込ロック内で本人と参加を確認し、最後に開いた家計簿だけを更新する。</summary>
    public async Task<LedgerOpenResult> OpenAsync(string ledgerId, CurrentSession current, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (!await IsCurrentAsync(current, token)) return new(LedgerOpenStatus.AuthenticationChanged);
        var ledger = await VisibleLedgers(current, ledgerId).SingleOrDefaultAsync(token);
        if (ledger is null) return new(LedgerOpenStatus.NotFound);
        var navigation = await context.Set<LedgerNavigation>().SingleOrDefaultAsync(
            row => row.AuthUserId == current.Session.AuthUserId, token);
        if (navigation is null)
            context.Add(new LedgerNavigation { AuthUserId = current.Session.AuthUserId, LastOpenedLedgerId = ledgerId });
        else navigation.LastOpenedLedgerId = ledgerId;
        await context.SaveChangesAsync(token);
        if (!await IsCurrentAsync(current, token)) return new(LedgerOpenStatus.AuthenticationChanged);
        ledger = await VisibleLedgers(current, ledgerId).SingleOrDefaultAsync(token);
        if (ledger is null) return new(LedgerOpenStatus.NotFound);
        await transaction.CommitAsync(token);
        return new(LedgerOpenStatus.Opened, ledger);
    }

    /// <summary>家計簿・作成者の管理者参加・作成要求を同じトランザクションで保存する。</summary>
    public async Task<LedgerCreationResult> CreateAsync(string name, Guid requestId, CurrentSession current, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (!await IsCurrentAsync(current, token)) return new(LedgerCreationStatus.AuthenticationChanged);
        var key = requestId.ToString("N");
        var previous = await context.Set<LedgerCreationRequest>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.RequestId == key, token);
        if (previous is not null)
        {
            if (previous.ActorAuthUserId != current.Session.AuthUserId || previous.RequestedName != name)
                return new(LedgerCreationStatus.Conflict);
            var replay = await VisibleLedgers(current, previous.LedgerId).SingleOrDefaultAsync(token);
            if (!await IsCurrentAsync(current, token)) return new(LedgerCreationStatus.AuthenticationChanged);
            return replay is null ? new(LedgerCreationStatus.NotFound) : new(LedgerCreationStatus.Replayed, replay);
        }
        var now = time.GetUtcNow().UtcDateTime;
        var ledger = new Ledger
        {
            Id = Guid.NewGuid().ToString("N"), Name = name, CreatedByAuthUserId = current.Session.AuthUserId,
            CreatedAtUtc = now, Revision = Guid.NewGuid().ToString("N")
        };
        context.Add(ledger);
        context.Add(new LedgerMember
        {
            LedgerId = ledger.Id, AuthUserId = current.Session.AuthUserId, Role = "Admin",
            JoinedAtUtc = now, Revision = Guid.NewGuid().ToString("N")
        });
        context.Add(new LedgerCreationRequest
        {
            RequestId = key, ActorAuthUserId = current.Session.AuthUserId, LedgerId = ledger.Id,
            RequestedName = name, CreatedAtUtc = now
        });
        await context.SaveChangesAsync(token);
        // 保存中に参加状態が変わっていても、作成者が管理者の家計簿だけを確定する。
        if (!await context.Set<LedgerMember>().AsNoTracking().AnyAsync(row => row.LedgerId == ledger.Id &&
            row.AuthUserId == current.Session.AuthUserId && row.Role == "Admin", token))
            throw new InvalidOperationException("家計簿の作成者の参加状態を確認できません。");
        if (!await IsCurrentAsync(current, token)) return new(LedgerCreationStatus.AuthenticationChanged);
        await transaction.CommitAsync(token);
        logger.LogInformation("家計簿を作成しました。実行者={ActorId} 家計簿ID={LedgerId} 日時={OccurredAtUtc}",
            current.Session.AuthUserId, ledger.Id, now);
        return new(LedgerCreationStatus.Created, new(ledger.Id, ledger.Name, "Admin", ledger.Revision));
    }

    /// <summary>現在のアカウントが参加する家計簿を選ぶ。</summary>
    public IQueryable<LedgerResponse> VisibleLedgers(CurrentSession current, string? ledgerId = null) =>
        from member in context.Set<LedgerMember>().AsNoTracking()
        join ledger in context.Set<Ledger>().AsNoTracking() on member.LedgerId equals ledger.Id
        where member.AuthUserId == current.Session.AuthUserId && (ledgerId == null || ledger.Id == ledgerId)
        orderby ledger.Name, ledger.Id
        select new LedgerResponse(ledger.Id, ledger.Name, member.Role, ledger.Revision);

    /// <summary>同じセッション・アカウントが現在も有効かDBの最新値で確認する。</summary>
    public Task<bool> IsCurrentAsync(CurrentSession current, CancellationToken token)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return (from session in context.Sessions.AsNoTracking()
                join user in context.Users.AsNoTracking() on session.AuthUserId equals user.Id
                where session.IdHash == current.Session.IdHash && user.Id == current.Session.AuthUserId &&
                    user.IsEnabled && session.RevokedAtUtc == null && session.ExpiresAtUtc > now
                select user.Id).AnyAsync(token);
    }
}
