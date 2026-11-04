using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Monetail.Windows.Client.Transport;

public sealed record MemberInfo(Guid AccountId, string? DisplayName, string Role, Guid? PersonId,
    string? PersonName, int? PersonDisplayOrder, DateTimeOffset JoinedAtUtc, Guid Revision);
public sealed record MembersInfo(MemberInfo[] Members, Guid CurrentAccountId, string CurrentRole);
public sealed record MemberPersonCandidate(Guid PersonId, string Name, bool IsSelectable);
public sealed record MemberEditInfo(MemberInfo Member, MemberPersonCandidate[] PersonCandidates);

/// <summary>参加者・家計簿招待の業務拒否。サーバーの任意の本文や秘密URLを表示しない。</summary>
public sealed class ParticipantRequestException(HttpStatusCode status, string? code, string? field,
    MemberEditInfo? latest = null, PeoplePermissions? permissions = null) : Exception("参加者・招待の操作を完了できませんでした。")
{
    public HttpStatusCode StatusCode { get; } = status;
    public string? Code { get; } = code;
    public string? Field { get; } = field;
    public MemberEditInfo? Latest { get; } = latest;
    public PeoplePermissions? Permissions { get; } = permissions;
}

/// <summary>コピー時だけURLを使用し、通常の文字列化で秘密を出さない。</summary>
public sealed record LedgerInvitationInfo(Guid InvitationId, string Url, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{
    public override string ToString() => $"Ledger invitation {InvitationId}";
}
public sealed record LedgerInvitationsInfo(LedgerInvitationInfo[] Invitations);

public sealed partial class MonetailApiClient
{
    public async Task<MembersInfo> ListMembersAsync(string access, Guid ledgerId, CancellationToken token = default)
    {
        using var json = await SendParticipantAsync(HttpMethod.Get, MemberPath(ledgerId), access, null, token);
        return ReadMembers(json.RootElement);
    }

    public async Task<MemberEditInfo> GetMemberEditAsync(string access, Guid ledgerId, Guid accountId, CancellationToken token = default)
    {
        using var json = await SendParticipantAsync(HttpMethod.Get, $"{MemberPath(ledgerId)}/{accountId:N}/edit", access, null, token);
        var result = ReadMemberEdit(json.RootElement);
        if (result.Member.AccountId != accountId) throw new ApiConnectionException();
        return result;
    }

    public async Task<MembersInfo> UpdateMemberAsync(string access, Guid ledgerId, MemberInfo member, string role,
        Guid? personId, bool isSelf, CancellationToken token = default)
    {
        if (role is not ("Admin" or "Editor" or "Viewer") || isSelf && role != member.Role) throw new ArgumentException("自分の役割は変更できません。");
        // 本人の人物対応変更には役割を含めない。
        using var json = await SendParticipantAsync(HttpMethod.Patch, $"{MemberPath(ledgerId)}/{member.AccountId:N}", access,
            isSelf ? new { member.Revision, personId } : (object)new { member.Revision, role, personId }, token);
        return ReadMembers(json.RootElement);
    }

    public async Task<MembersInfo> ExcludeMemberAsync(string access, Guid ledgerId, MemberInfo member, CancellationToken token = default)
    {
        using var json = await SendParticipantAsync(HttpMethod.Delete, $"{MemberPath(ledgerId)}/{member.AccountId:N}", access, new { member.Revision }, token);
        return ReadMembers(json.RootElement);
    }

    public async Task<LedgerInvitationsInfo> ListInvitationsAsync(string access, Guid ledgerId, CancellationToken token = default)
    {
        using var json = await SendParticipantAsync(HttpMethod.Get, InvitationPath(ledgerId), access, null, token);
        return ReadInvitations(json.RootElement);
    }
    public async Task<LedgerInvitationsInfo> CreateInvitationAsync(string access, Guid ledgerId, Guid requestId, CancellationToken token = default)
    {
        using var json = await SendParticipantAsync(HttpMethod.Post, InvitationPath(ledgerId), access, new { requestId }, token);
        if (!json.RootElement.TryGetProperty("invitation", out var created)) throw new ApiConnectionException();
        var issued = ReadInvitation(created);
        var result = ReadInvitations(json.RootElement);
        if (!result.Invitations.Any(row => row == issued)) throw new ApiConnectionException();
        return result;
    }
    public async Task<LedgerInvitationsInfo> CancelLedgerInvitationAsync(string access, Guid ledgerId, Guid id, CancellationToken token = default)
    {
        using var json = await SendParticipantAsync(HttpMethod.Delete, $"{InvitationPath(ledgerId)}/{id:N}", access, null, token);
        return ReadInvitations(json.RootElement);
    }
    private static string MemberPath(Guid ledgerId) => $"ledgers/{ledgerId:N}/members";
    private static string InvitationPath(Guid ledgerId) => $"ledgers/{ledgerId:N}/invitations";
    private async Task<JsonDocument> SendParticipantAsync(HttpMethod method, string path, string access, object? input, CancellationToken token)
    {
        using var body = input is null ? null : JsonContent.Create(input);
        return Parse(await SendAsync(method, path, access, null, false, token, body, ReadParticipantFailure));
    }
    private static ParticipantRequestException ReadParticipantFailure(HttpStatusCode status, byte[] bytes)
    {
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests)
        {
            var error = ReadPeopleFailure(status, bytes, false);
            return new(status, error.Code, error.Field, permissions: error.Permissions);
        }
        using var json = Parse(bytes);
        var code = String(json.RootElement, "code");
        MemberEditInfo? latest = null;
        if (status == HttpStatusCode.Conflict && code == "revision_conflict")
        {
            if (!json.RootElement.TryGetProperty("latest", out var item)) throw new ApiConnectionException();
            latest = ReadMemberEdit(item);
        }
        return new(status, code, String(json.RootElement, "field"), latest);
    }
    private static MembersInfo ReadMembers(JsonElement json)
    {
        if (!json.TryGetProperty("members", out var rows) || rows.ValueKind != JsonValueKind.Array ||
            String(json, "currentRole") is not ("Viewer" or "Editor" or "Admin")) throw new ApiConnectionException();
        // 順序はAPIの契約。クライアントで並べ替えない。
        var members = rows.EnumerateArray().Select(ReadMember).ToArray();
        var me = ReadUuidProperty(json, "currentAccountId");
        if (members.Select(row => row.AccountId).Distinct().Count() != members.Length ||
            !members.Any(row => row.AccountId == me && row.Role == String(json, "currentRole"))) throw new ApiConnectionException();
        return new(members, me, String(json, "currentRole")!);
    }
    private static MemberInfo ReadMember(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || String(json, "role") is not ("Viewer" or "Editor" or "Admin") ||
            !json.TryGetProperty("personId", out var person) || !Date(json, "joinedAtUtc", out var joined)) throw new ApiConnectionException();
        Guid? personId = person.ValueKind == JsonValueKind.Null ? null : ReadUuid(person);
        var personName = String(json, "personName");
        int? order = null;
        if (!json.TryGetProperty("personDisplayOrder", out var position)) throw new ApiConnectionException();
        if (position.ValueKind != JsonValueKind.Null)
        {
            if (position.ValueKind != JsonValueKind.Number || !position.TryGetInt32(out var value) || value < 0) throw new ApiConnectionException();
            order = value;
        }
        if (personId is null && (personName is not null || order is not null) ||
            personId is not null && (string.IsNullOrWhiteSpace(personName) || order is null)) throw new ApiConnectionException();
        return new(ReadUuidProperty(json, "accountId"), String(json, "displayName"), String(json, "role")!, personId,
            personName, order, joined, ReadUuidProperty(json, "revision"));
    }
    private static MemberEditInfo ReadMemberEdit(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("member", out var member) ||
            !json.TryGetProperty("personCandidates", out var rows) || rows.ValueKind != JsonValueKind.Array) throw new ApiConnectionException();
        var candidates = rows.EnumerateArray().Select(row =>
        {
            if (row.ValueKind != JsonValueKind.Object || String(row, "name") is not { Length: > 0 and <= 100 } name || !row.TryGetProperty("isSelectable", out var selectable) ||
                selectable.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ApiConnectionException();
            return new MemberPersonCandidate(ReadUuidProperty(row, "personId"), name, selectable.GetBoolean());
        }).ToArray();
        if (candidates.Select(row => row.PersonId).Distinct().Count() != candidates.Length) throw new ApiConnectionException();
        return new(ReadMember(member), candidates);
    }
    private LedgerInvitationsInfo ReadInvitations(JsonElement json)
    {
        if (!json.TryGetProperty("invitations", out var rows) || rows.ValueKind != JsonValueKind.Array) throw new ApiConnectionException();
        var invitations = rows.EnumerateArray().Select(ReadInvitation).ToArray();
        if (invitations.Select(row => row.InvitationId).Distinct().Count() != invitations.Length) throw new ApiConnectionException();
        return new(invitations);
    }
    private LedgerInvitationInfo ReadInvitation(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !Uri.TryCreate(String(json, "url"), UriKind.Absolute, out var url) ||
            url.GetLeftPart(UriPartial.Authority) != endpoint.BaseUri.GetLeftPart(UriPartial.Authority) ||
            url.AbsolutePath != "/ledger-invitation" || url.Query.Length != 0 || url.UserInfo.Length != 0 ||
            !url.Fragment.StartsWith("#token=", StringComparison.Ordinal) || url.Fragment.Length != 50 ||
            url.Fragment[7..].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) ||
            !Date(json, "createdAtUtc", out var created) || !Date(json, "expiresAtUtc", out var expires) || expires <= created)
            throw new ApiConnectionException();
        return new(ReadUuidProperty(json, "invitationId"), url.AbsoluteUri, created, expires);
    }
}
