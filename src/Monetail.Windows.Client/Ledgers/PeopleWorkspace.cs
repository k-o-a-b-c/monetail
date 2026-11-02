using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

/// <summary>正規化済みの入力。同じ作成内容の通信再試行では同じ要求IDを維持する。</summary>
public sealed record PersonInput
{
    public string Name { get; }
    public DateOnly? ValidFrom { get; }
    public DateOnly? ValidTo { get; }
    public PersonInput(string name, DateOnly? validFrom, DateOnly? validTo)
    {
        Name = name.Trim();
        if (Name.Length == 0) throw new PersonInputException("name", "人物名を入力してください。");
        if (Name.Length > 100) throw new PersonInputException("name", "人物名は100文字以内で入力してください。");
        if (validFrom > validTo) throw new PersonInputException("validTo", "有効開始日は有効終了日以前の日付にしてください。");
        ValidFrom = validFrom;
        ValidTo = validTo;
    }
}

public sealed class PersonInputException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed record PersonCreationDraft
{
    public PersonInput Input { get; }
    public Guid RequestId { get; } = Guid.NewGuid();
    public PersonCreationDraft(PersonInput input) => Input = input;
}

/// <summary>人物管理でも既存の認証更新・別ウィンドウのログイン変更検知を共用する。</summary>
public sealed class PeopleWorkspace(MonetailApiClient api, ClientSession session, Guid ledgerId)
{
    public Task<PeopleInfo> ListAsync(CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.ListPeopleAsync(access, ledgerId, ct), token);

    public Task<PeopleInfo> CreateAsync(PersonCreationDraft draft, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.CreatePersonAsync(access, ledgerId, draft.RequestId,
            draft.Input.Name, draft.Input.ValidFrom, draft.Input.ValidTo, ct), token);

    public Task<PeopleInfo> UpdateAsync(PersonInfo person, PersonInput input, bool includePeriod, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.UpdatePersonAsync(access, ledgerId, person.PersonId, person.Revision,
            input.Name, includePeriod, input.ValidFrom, input.ValidTo, ct), token);

    public Task<PeopleInfo> DeleteAsync(PersonInfo person, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.DeletePersonAsync(access, ledgerId, person.PersonId, person.Revision, ct), token);

    public Task<PeopleInfo> SaveOrderAsync(Guid revision, IReadOnlyList<Guid> personIds, CancellationToken token = default) =>
        session.ExecuteAuthenticatedAsync((access, ct) => api.SavePeopleOrderAsync(access, ledgerId, revision, personIds, ct), token);
}
