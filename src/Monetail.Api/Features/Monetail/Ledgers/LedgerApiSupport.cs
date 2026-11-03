using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Operations;
using Monetail.Api.Persistence;

namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>利用者へ公開できる業務エラーだけをHTTP境界へ渡す。</summary>
public sealed class LedgerApiFault(int status, string code, string message, string? fieldName = null, object? latest = null,
    LedgerPermissions? permissions = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public bool HasLatest => latest is not null;
    public LedgerPermissions? Permissions { get; private set; } = permissions;
    public object Body => new { code, message = Message, field = fieldName, latest, permissions = Permissions };
    public LedgerApiFault WithPermissions(LedgerPermissions current)
    {
        Permissions = current;
        return this;
    }
}

/// <summary>操作者自身の現在の参加・役割・人物対応だけを返す。未参加は両方null。</summary>
public sealed record LedgerPermissions(string? CurrentRole, string? MyPersonId);

/// <summary>業務エラーと保存障害を分け、DBの詳細や招待トークンを公開しない。</summary>
public sealed class LedgerApiFilter(ILogger<LedgerApiFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        try { return await next(invocation); }
        catch (LedgerApiFault error) { return Results.Json(error.Body, statusCode: error.Status); }
        catch (Exception error) when (error is DbUpdateException or SqliteException or InvalidOperationException)
        {
            logger.LogWarning("家計簿の保存処理に失敗しました。エラー種類={ErrorType}", error.GetType().Name);
            return Results.Json(new { code = "store_unavailable", message = "保存先を利用できません。時間をおいてやり直してください。" }, statusCode: 503);
        }
    }
}

/// <summary>項目省略とnullを区別し、許可したJSON本文だけを受け付ける。</summary>
public sealed class LedgerPayload(JsonElement root)
{
    public static async Task<LedgerPayload> ReadAsync(HttpContext http, params string[] allowed)
    {
        try
        {
            if (!http.Request.HasJsonContentType()) throw Bad("JSON形式で入力内容を指定してください。");
            var root = await http.Request.ReadFromJsonAsync<JsonElement>(http.RequestAborted);
            if (root.ValueKind != JsonValueKind.Object) throw Bad("入力内容を確認してください。");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                    throw Bad("入力項目を確認してください。");
            return new(root);
        }
        catch (JsonException) { throw Bad("入力内容を確認してください。"); }
    }

    public bool Has(string name) => root.TryGetProperty(name, out _);
    public JsonElement Value(string name) => root.TryGetProperty(name, out var value) ? value : default;
    public string Text(string name)
    {
        var value = Value(name);
        if (value.ValueKind != JsonValueKind.String) throw Bad("文字列を指定してください。", name);
        return value.GetString()!;
    }
    public string Uuid(string name)
    {
        if (!Guid.TryParse(Text(name), out var id) || id == Guid.Empty) throw Bad("有効な識別子を指定してください。", name);
        return id.ToString("N");
    }
    public DateOnly? Date(string name)
    {
        if (Value(name).ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (!DateOnly.TryParseExact(Text(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw Bad("正しい日付をYYYY-MM-DDで指定してください。", name);
        return date;
    }
    public static LedgerApiFault Bad(string message, string? field = null) => new(400, "invalid_input", message, field);
}

public sealed record PersonInfo(string PersonId, string Name, DateOnly? ValidFrom, DateOnly? ValidTo, int DisplayOrder, string Revision);
public sealed record PeopleResponse(PersonInfo[] People, string? MyPersonId, string CurrentRole, string OrderRevision);
public sealed record MemberInfo(string AccountId, string? DisplayName, string Role, string? PersonId, string? PersonName,
    int? PersonDisplayOrder, DateTime JoinedAtUtc, string Revision);
public sealed record MembersResponse(MemberInfo[] Members, string CurrentAccountId, string CurrentRole);
public sealed record PersonCandidate(string PersonId, string Name, bool IsSelectable);
public sealed record MemberEditResponse(MemberInfo Member, PersonCandidate[] PersonCandidates);

/// <summary>現在の参加・権限を前後で照合し、要求ごとに保存を原子的に確定する。</summary>
public sealed partial class LedgerManagementService(ApiDbContext context, LedgerService ledgers, TimeProvider time, PasskeySettings settings)
{
    private static string Version() => Guid.NewGuid().ToString("N");
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    private static LedgerApiFault Missing() => new(404, "not_found", "対象が見つからないか、この家計簿に参加していません。");
    private static LedgerApiFault Forbidden() => new(403, "forbidden", "この操作を行う権限がありません。");
    private static LedgerApiFault Conflict(object latest) => new(409, "revision_conflict", "ほかの操作で変更されています。最新の内容を確認してください。", latest: latest);

    private async Task<LedgerMember> RequireMemberAsync(string ledgerId, CurrentSession actor, Func<LedgerMember, bool> permission, CancellationToken token)
    {
        await RequireLoginAsync(actor, token);
        var member = await context.Set<LedgerMember>().AsNoTracking().SingleOrDefaultAsync(
            row => row.LedgerId == ledgerId && row.AuthUserId == actor.Session.AuthUserId, token)
            ?? throw Missing().WithPermissions(new(null, null));
        if (!permission(member)) throw Forbidden().WithPermissions(new(member.Role, member.PersonId));
        return member;
    }

    private async Task RequireLoginAsync(CurrentSession actor, CancellationToken token)
    {
        if (!await ledgers.IsCurrentAsync(actor, token)) throw new LedgerApiFault(401, "authentication_changed", "ログインを継続できません。ログインし直してください。");
    }

    private async Task<object> InLedgerAsync(string ledgerId, CurrentSession actor, Func<LedgerMember, bool> permission,
        Func<LedgerMember, Task<object>> action, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var member = await RequireMemberAsync(ledgerId, actor, permission, token);
        object result;
        try { result = await action(member); }
        catch (LedgerApiFault error)
        {
            // 競合による早期応答でも、最新情報を読み込んだ後の本人・権限を確認する。
            if (error.HasLatest) await RequireMemberAsync(ledgerId, actor, permission, token);
            if (error.Status is 403 or 404 && error.Permissions is null)
                error.WithPermissions(new(member.Role, member.PersonId));
            throw;
        }
        await RequireMemberAsync(ledgerId, actor, permission, token);
        await transaction.CommitAsync(token);
        return result;
    }

    private async Task<OperationRequest?> PreviousAsync(string requestId, string operation, string hash, CurrentSession actor, CancellationToken token)
    {
        var previous = await context.Set<OperationRequest>().AsNoTracking().SingleOrDefaultAsync(row => row.RequestId == requestId, token);
        if (previous is not null && !previous.Matches(operation, actor.Session.AuthUserId, hash))
            throw new LedgerApiFault(409, "request_conflict", "この要求IDは別の作成操作で使用済みです。");
        return previous;
    }

    private void Record(string requestId, string operation, string hash, string resultId, CurrentSession actor) => context.Add(new OperationRequest
    {
        RequestId = requestId, OperationType = operation, RequestHash = hash, ResultId = resultId,
        ActorAuthUserId = actor.Session.AuthUserId, CreatedAtUtc = Now
    });

    private async Task<PeopleResponse> PeopleAsync(string ledgerId, CurrentSession actor, CancellationToken token)
    {
        var people = await context.Set<LedgerPerson>().AsNoTracking().Where(row => row.LedgerId == ledgerId)
            .OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id)
            .Select(row => new PersonInfo(row.Id, row.Name, row.ValidFrom, row.ValidTo, row.DisplayOrder, row.Revision)).ToArrayAsync(token);
        var member = await RequireMemberAsync(ledgerId, actor, _ => true, token);
        var revision = await context.Set<Ledger>().AsNoTracking().Where(row => row.Id == ledgerId).Select(row => row.PeopleOrderRevision).SingleAsync(token);
        return new(people, member.PersonId, member.Role, revision);
    }

    private IQueryable<MemberInfo> MemberRows(string ledgerId, string? accountId = null) =>
        from member in context.Set<LedgerMember>().AsNoTracking()
        join account in context.Users.AsNoTracking() on member.AuthUserId equals account.Id
        join person in context.Set<LedgerPerson>().AsNoTracking() on new { member.LedgerId, Id = member.PersonId }
            equals new { person.LedgerId, Id = (string?)person.Id } into matches
        from person in matches.DefaultIfEmpty()
        where member.LedgerId == ledgerId && (accountId == null || member.AuthUserId == accountId)
        select new MemberInfo(member.AuthUserId, account.DisplayName, member.Role, member.PersonId,
            person == null ? null : person.Name, person == null ? null : person.DisplayOrder,
            DateTime.SpecifyKind(member.JoinedAtUtc, DateTimeKind.Utc), member.Revision);

    private async Task<MembersResponse> MembersAsync(string ledgerId, CurrentSession actor, CancellationToken token)
    {
        var member = await RequireMemberAsync(ledgerId, actor, row => row.Role == "Admin", token);
        var rows = await MemberRows(ledgerId).ToArrayAsync(token);
        return new(rows.OrderByDescending(row => row.AccountId == member.AuthUserId)
            .ThenByDescending(row => row.PersonId != null).ThenBy(row => row.PersonDisplayOrder)
            .ThenBy(row => row.JoinedAtUtc).ThenBy(row => row.AccountId, StringComparer.Ordinal).ToArray(), member.AuthUserId, member.Role);
    }
}
