using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Monetail.Windows.Client.Transport;

public sealed record PersonInfo(Guid PersonId, string Name, DateOnly? ValidFrom, DateOnly? ValidTo, int DisplayOrder, Guid Revision);
public sealed record PeopleInfo(PersonInfo[] People, Guid? MyPersonId, string CurrentRole, Guid OrderRevision);

/// <summary>人物管理の業務エラー。最新情報だけを型付きで受け取り、表示文はクライアントで選ぶ。</summary>
public sealed class PeopleRequestException(HttpStatusCode statusCode, string? code, string? field,
    PersonInfo? latestPerson = null, PeopleInfo? latestPeople = null) : Exception("人物の操作を完了できませんでした。")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
    public string? Field { get; } = field;
    public PersonInfo? LatestPerson { get; } = latestPerson;
    public PeopleInfo? LatestPeople { get; } = latestPeople;
}

public sealed partial class MonetailApiClient
{
    public Task<PeopleInfo> ListPeopleAsync(string accessToken, Guid ledgerId, CancellationToken token = default) =>
        SendPeopleAsync(HttpMethod.Get, PeoplePath(ledgerId), accessToken, null, false, token);

    public Task<PeopleInfo> CreatePersonAsync(string accessToken, Guid ledgerId, Guid requestId, string name,
        DateOnly? validFrom, DateOnly? validTo, CancellationToken token = default) =>
        SendPeopleAsync(HttpMethod.Post, PeoplePath(ledgerId), accessToken,
            new { requestId, name, validFrom, validTo }, false, token);

    /// <summary>本人の名前だけの変更では、管理者専用の期間項目を本文へ含めない。</summary>
    public Task<PeopleInfo> UpdatePersonAsync(string accessToken, Guid ledgerId, Guid personId, Guid revision, string name,
        bool includePeriod, DateOnly? validFrom, DateOnly? validTo, CancellationToken token = default) =>
        SendPeopleAsync(HttpMethod.Patch, $"{PeoplePath(ledgerId)}/{personId:N}", accessToken,
            includePeriod ? new { revision, name, validFrom, validTo } : (object)new { revision, name }, true, token);

    public Task<PeopleInfo> DeletePersonAsync(string accessToken, Guid ledgerId, Guid personId, Guid revision,
        CancellationToken token = default) =>
        SendPeopleAsync(HttpMethod.Delete, $"{PeoplePath(ledgerId)}/{personId:N}", accessToken, new { revision }, true, token);

    public Task<PeopleInfo> SavePeopleOrderAsync(string accessToken, Guid ledgerId, Guid revision,
        IReadOnlyList<Guid> personIds, CancellationToken token = default) =>
        SendPeopleAsync(HttpMethod.Put, $"{PeoplePath(ledgerId)}/order", accessToken, new { revision, personIds }, false, token);

    private static string PeoplePath(Guid ledgerId) => $"ledgers/{ledgerId:N}/people";

    private async Task<PeopleInfo> SendPeopleAsync(HttpMethod method, string path, string access, object? input,
        bool personConflict, CancellationToken token)
    {
        using var body = input is null ? null : JsonContent.Create(input);
        var bytes = await SendAsync(method, path, access, null, false, token, body,
            (status, bytes) => ReadPeopleFailure(status, bytes, personConflict));
        using var document = Parse(bytes);
        return ReadPeople(document.RootElement);
    }

    private static PeopleRequestException ReadPeopleFailure(HttpStatusCode status, byte[] bytes, bool personConflict)
    {
        // 回数制限や共通の権限拒否は、業務エラー本文がなくても区別する。
        if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return new(status, null, null);
        using var document = Parse(bytes);
        var json = document.RootElement;
        var code = String(json, "code");
        var field = String(json, "field");
        if (code == "revision_conflict" && status == HttpStatusCode.Conflict)
        {
            if (!json.TryGetProperty("latest", out var latest)) throw new ApiConnectionException();
            return personConflict ? new(status, code, field, latestPerson: ReadPerson(latest)) :
                new(status, code, field, latestPeople: ReadPeople(latest));
        }
        return new(status, code, field);
    }

    private static PeopleInfo ReadPeople(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("people", out var rows) || rows.ValueKind != JsonValueKind.Array ||
            String(json, "currentRole") is not ("Viewer" or "Editor" or "Admin") ||
            !json.TryGetProperty("myPersonId", out var mine)) throw new ApiConnectionException();
        var people = rows.EnumerateArray().Select(ReadPerson).ToArray();
        Guid? myId = mine.ValueKind == JsonValueKind.Null ? null : ReadUuid(mine);
        if (people.Select(row => row.PersonId).Distinct().Count() != people.Length ||
            people.Select(row => row.DisplayOrder).Distinct().Count() != people.Length ||
            myId is not null && !people.Any(row => row.PersonId == myId)) throw new ApiConnectionException();
        return new(people, myId, String(json, "currentRole")!, ReadUuidProperty(json, "orderRevision"));
    }

    private static PersonInfo ReadPerson(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || String(json, "name") is not { Length: > 0 and <= 100 } name ||
            string.IsNullOrWhiteSpace(name) || !json.TryGetProperty("displayOrder", out var order) ||
            order.ValueKind != JsonValueKind.Number || !order.TryGetInt32(out var position) || position < 0) throw new ApiConnectionException();
        var from = ReadPersonDate(json, "validFrom");
        var to = ReadPersonDate(json, "validTo");
        if (from > to) throw new ApiConnectionException();
        return new(ReadUuidProperty(json, "personId"), name, from, to, position, ReadUuidProperty(json, "revision"));
    }

    private static Guid ReadUuidProperty(JsonElement json, string field) =>
        json.TryGetProperty(field, out var value) ? ReadUuid(value) : throw new ApiConnectionException();

    private static Guid ReadUuid(JsonElement value) => value.ValueKind == JsonValueKind.String &&
        Guid.TryParse(value.GetString(), out var id) && id != Guid.Empty ? id : throw new ApiConnectionException();

    private static DateOnly? ReadPersonDate(JsonElement json, string field)
    {
        if (!json.TryGetProperty(field, out var value)) throw new ApiConnectionException();
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new ApiConnectionException();
    }
}
