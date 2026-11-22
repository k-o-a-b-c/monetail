using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Management;
public sealed record ParticipantPersonChoice(Guid? PersonId, string Name, bool IsSelectable);
/// <summary>参加者の編集と招待要求を保持する。招待の再送下書きは通常の一覧移動でも失わない。</summary>
public sealed partial class ParticipantsManagementViewModel : ObservableObject, IDisposable
{
    public ManagementContext Context { get; }
    public MembersInfo? Info { get; private set; }
    public MemberEditInfo? Editing { get; private set; }
    public MemberEditInfo? Latest { get; private set; }
    public LedgerInvitationsInfo? Invitations { get; private set; }
    public LedgerInvitationDraft? InvitationDraft { get; private set; }
    public LedgerInvitationInfo? Cancelling { get; private set; }
    public ObservableCollection<ParticipantRow> Rows { get; } = [];
    public ObservableCollection<LedgerInvitationRow> InvitationRows { get; } = [];
    public ParticipantPersonChoice[] PersonChoices { get; private set; } = [];

    private bool editorDenied, accessDenied;
    private CancellationToken readToken;
    [ObservableProperty]
    public partial bool IsInvitations { get; private set; }

    [ObservableProperty]
    public partial bool IsExcluding { get; private set; }

    [ObservableProperty]
    public partial string Role { get; set; } = "Viewer";

    [ObservableProperty]
    public partial ParticipantPersonChoice? SelectedPerson { get; set; }

    [ObservableProperty]
    public partial string? EditorError { get; private set; }

    [ObservableProperty]
    public partial string? PersonError { get; private set; }

    [ObservableProperty]
    public partial string? ListError { get; private set; }

    [ObservableProperty]
    public partial string? InvitationNotice { get; private set; }

    [ObservableProperty]
    public partial string? CancelError { get; private set; }

    public event Func<Task>? EditorRequested;
    public event Func<Task>? SaveRequested;
    public ParticipantsManagementViewModel(ManagementContext context)
    {
        Context = context;
        context.PropertyChanged += ContextChanged;
    }

    public bool CanManage => !Context.IsDisposed && !accessDenied && Context.Permissions.CurrentRole == "Admin";
    public bool HasEditor => Editing is not null;
    public bool HasLatest => Latest is not null;
    public bool IsSelf => Editing?.Member.AccountId == Context.AccountId;
    public bool CanSave => Context.CanOperate && CanManage && !editorDenied && !HasLatest && HasEditor;
    public bool CanCancelInvitation => Context.CanOperate && CanManage && Cancelling is not null;
    public bool CanRestoreAuthentication => HasEditor && !IsExcluding || IsInvitations && InvitationDraft is not null;
    public bool HasInvitationDraft => InvitationDraft is not null;
    public bool EmptyInvitations => InvitationRows.Count == 0;
    public string Title => IsExcluding ? "参加者の除外" : "参加者の編集";
    public string PrimaryText => IsExcluding ? "除外する" : "保存";
    public string Name => Editing?.Member.DisplayName ?? "表示名未設定";
    public string OwnRole => $"家計簿内の役割：{ParticipantRow.RoleName(Editing?.Member.Role ?? "Viewer")}（本人の役割は変更できません）";
    public string ExcludeNotice => $"「{Name}」をこの家計簿から除外しますか。アカウント・人物・過去の記録は削除しません。";
    public string LatestText => Latest is not { } latest ? "" : $"アカウント：{latest.Member.DisplayName ?? "表示名未設定"}\n役割：{ParticipantRow.RoleName(latest.Member.Role)}\n対応する人物：{latest.Member.PersonName ?? "未設定"}\n参加日時：{latest.Member.JoinedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}\n人物候補：{string.Join("、", latest.PersonCandidates.Select(p => p.Name + (p.IsSelectable ? "" : "（設定不可）")))}";
    public string CancelTarget => $"有効期限が{Cancelling?.ExpiresAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}の招待リンクを取り消しますか。既に参加した人は除外されません。";

    private bool CanOperate() => Context.CanOperate && CanManage && !HasEditor && Cancelling is null;
    private bool CanEdit(MemberInfo? row) => row is not null && CanOperate();
    private bool CanExclude(MemberInfo? row) => CanEdit(row) && row!.AccountId != Context.AccountId;
    private bool CanUseInvitation(LedgerInvitationInfo? row) => row is not null && CanOperate();
    private void ContextChanged(object? sender, PropertyChangedEventArgs args) => NotifyState();
    public void ActivateReads(CancellationToken token) => readToken = token;
    public Task LoadAsync(CancellationToken token)
    {
        readToken = token;
        return Context.RunAsync(async () =>
        {
            try
            {
                var result = await Context.Participants.ListAsync(token);
                if (!token.IsCancellationRequested)
                    Apply(result);
            }
            catch (ParticipantRequestException error)
            {
                if (!token.IsCancellationRequested)
                {
                    ApplyFailure(error);
                    ListError = Message(error);
                }
            }
        }, message =>
        {
            if (!token.IsCancellationRequested)
                ListError = message;
        });
    }

    public void Apply(MembersInfo info)
    {
        if (Context.IsDisposed)
            return;
        Info = info;
        var person = info.Members.FirstOrDefault(row => row.AccountId == info.CurrentAccountId)?.PersonId;
        Context.ApplyPermissions(new(info.CurrentRole, person));
        Context.Masters.UpdateIdentity(Context.Ledger.LedgerId, person);
        Rows.Clear();
        foreach (var member in info.Members)
            Rows.Add(new(member, member.AccountId == info.CurrentAccountId));
        ListError = null;
        NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync(MemberInfo? member)
    {
        MemberEditInfo? result = null;
        await Context.RunAsync(async () =>
        {
            try
            {
                result = await Context.Participants.EditAsync(member!.AccountId, readToken);
            }
            catch (ParticipantRequestException error)
            {
                ApplyFailure(error);
                ListError = Message(error);
            }
        }, message => ListError = message);
        if (result is not null && Context.CanOperate && !readToken.IsCancellationRequested)
            await OpenEditorAsync(result, false);
    }

    [RelayCommand(CanExecute = nameof(CanExclude))]
    private Task ExcludeAsync(MemberInfo? member) => OpenEditorAsync(new(member!, []), true);
    private Task OpenEditorAsync(MemberEditInfo edit, bool excluding)
    {
        Editing = edit;
        Latest = null;
        IsExcluding = excluding;
        editorDenied = false;
        EditorError = PersonError = null;
        FillFields(edit);
        NotifyState();
        return EditorRequested?.Invoke() ?? Task.CompletedTask;
    }

    private void FillFields(MemberEditInfo edit)
    {
        Role = edit.Member.Role;
        PersonChoices = new[]
        {
            new ParticipantPersonChoice(null, "未設定", true)
        }.Concat(edit.PersonCandidates.Select(p => new ParticipantPersonChoice(p.PersonId, p.Name, p.IsSelectable))).ToArray();
        OnPropertyChanged(nameof(PersonChoices));
        SelectedPerson = PersonChoices.FirstOrDefault(p => p.PersonId == edit.Member.PersonId);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => SaveRequested?.Invoke() ?? Task.CompletedTask;
    public async Task<bool> SaveEditorAsync()
    {
        if (!CanSave || Editing is not { } edit || !IsExcluding && (Role is not ("Admin" or "Editor" or "Viewer") || SelectedPerson is not { IsSelectable: true }))
            return false;
        var saved = false;
        EditorError = PersonError = null;
        await Context.RunAsync(async () =>
        {
            try
            {
                var result = IsExcluding ? await Context.Participants.ExcludeAsync(edit.Member, Context.AccountId, Context.Token) : await Context.Participants.SaveAsync(edit.Member, IsSelf ? edit.Member.Role : Role, SelectedPerson?.PersonId, Context.AccountId, Context.Token);
                Apply(result);
                saved = !Context.IsDisposed;
            }
            catch (ParticipantRequestException error)
            {
                if (error.Latest is { } latest)
                {
                    if (latest.Member.AccountId != edit.Member.AccountId)
                        EditorError = "応答を確認できませんでした。入力は保持しています。";
                    else
                        Latest = latest;
                }
                else
                {
                    ApplyFailure(error);
                    editorDenied = error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound;
                    if (error.Code == "person_already_assigned")
                        PersonError = "この人物は別の参加者に設定されています。人物を選び直してください。";
                    else
                        EditorError = Message(error);
                }

                NotifyState();
            }
        }, message => EditorError = message);
        return saved;
    }

    [RelayCommand]
    private void AdoptLatest()
    {
        if (!Context.CanOperate || Latest is not { } latest)
            return;
        Editing = latest;
        Latest = null;
        FillFields(latest);
        EditorError = PersonError = null;
        NotifyState();
    }

    public void CloseEditor()
    {
        Editing = Latest = null;
        IsExcluding = false;
        EditorError = PersonError = null;
        NotifyState();
    }

    public void ApplyFailure(ParticipantRequestException error)
    {
        if (error.Permissions is { } permissions)
        {
            Context.ApplyPermissions(permissions);
            accessDenied = permissions.CurrentRole != "Admin";
            if (permissions.CurrentRole is null)
            {
                Rows.Clear();
                InvitationRows.Clear();
            }
        }
        else if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            accessDenied = true;
        NotifyState();
    }

    private static string Message(ParticipantRequestException error) => error.StatusCode switch
    {
        HttpStatusCode.Forbidden => "この操作を行う権限がありません。入力内容は保存されていません。",
        HttpStatusCode.NotFound => "対象が存在しないか、この家計簿に参加していません。入力内容は保存されていません。",
        HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
        _ => error.Code switch
        {
            "self_role_change" => "自分の役割は変更できません。別の管理者に依頼してください。",
            "self_exclusion" => "自分は除外できません。別の管理者に依頼してください。",
            "request_conflict" => "この招待発行の要求を続けられません。内容を確認してください。",
            "invitation_expired" or "invitation_revoked" or "invitation_used" => "この招待リンクは期限切れ・取消済み・使用済みです。",
            _ => "操作を完了できませんでした。入力内容を確認して再試行してください。"
        }};
    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task OpenInvitationsAsync()
    {
        IsInvitations = true;
        await LoadInvitationsAsync(readToken);
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        if (!Context.CanOperate)
            return;
        IsInvitations = false;
        if (CanManage)
            await LoadAsync(readToken);
    }

    private Task LoadInvitationsAsync(CancellationToken token) => Context.RunAsync(async () =>
    {
        try
        {
            var result = await Context.Participants.InvitationsAsync(token);
            if (!token.IsCancellationRequested)
                ApplyInvitations(result);
        }
        catch (ParticipantRequestException error)
        {
            ApplyFailure(error);
            InvitationNotice = Message(error);
        }
    }, message => InvitationNotice = message);
    public void ApplyInvitations(LedgerInvitationsInfo info)
    {
        if (Context.IsDisposed)
            return;
        Invitations = info;
        RenderInvitations();
    }

    private void RenderInvitations()
    {
        InvitationRows.Clear();
        foreach (var invitation in (Invitations?.Invitations ?? []).Where(i => i.ExpiresAtUtc > DateTimeOffset.UtcNow).OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.InvitationId))
            InvitationRows.Add(new(invitation));
        NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task IssueAsync()
    {
        InvitationDraft ??= new();
        NotifyState();
        await Context.RunAsync(async () =>
        {
            try
            {
                var result = await Context.Participants.CreateInvitationAsync(InvitationDraft, Context.Token);
                InvitationDraft = null;
                ApplyInvitations(result);
                InvitationNotice = "招待リンクを発行しました。";
            }
            catch (ParticipantRequestException error)
            {
                ApplyFailure(error);
                InvitationNotice = Message(error);
            }
        }, message => InvitationNotice = message);
        NotifyState();
    }

    public bool CanCopy(LedgerInvitationInfo invitation)
    {
        if (!CanUseInvitation(invitation))
            return false;
        if (invitation.ExpiresAtUtc > DateTimeOffset.UtcNow)
            return true;
        RenderInvitations();
        InvitationNotice = "この招待リンクは期限切れです。";
        return false;
    }

    public void Copied(bool success) => InvitationNotice = success ? "招待リンクをコピーしました。送付先を限定してください。" : "コピーできませんでした。クリップボードへのアクセスを確認してください。";
    [RelayCommand(CanExecute = nameof(CanUseInvitation))]
    private Task CancelAsync(LedgerInvitationInfo? invitation)
    {
        Cancelling = invitation;
        CancelError = null;
        NotifyState();
        return EditorRequested?.Invoke() ?? Task.CompletedTask;
    }

    public async Task<bool> CancelInvitationAsync()
    {
        if (!CanCancelInvitation || Cancelling is not { } invitation)
            return false;
        var saved = false;
        await Context.RunAsync(async () =>
        {
            try
            {
                ApplyInvitations(await Context.Participants.CancelInvitationAsync(invitation.InvitationId, Context.Token));
                saved = !Context.IsDisposed;
            }
            catch (ParticipantRequestException error)
            {
                ApplyFailure(error);
                CancelError = Message(error);
            }
        }, message => CancelError = message);
        return saved;
    }

    public void CloseCancellation()
    {
        Cancelling = null;
        CancelError = null;
        NotifyState();
    }

    public void ClearListError() => ListError = null;
    public void Leave()
    {
        CloseEditor();
        CloseCancellation();
        IsInvitations = false;
    }

    public async Task ResumeAsync(string? denied, CancellationToken token)
    {
        if (denied is null && Context.Ledger.Role == "Admin")
        {
            try
            {
                if (HasEditor)
                    Apply(await Context.Participants.ListAsync(token));
                else
                    ApplyInvitations(await Context.Participants.InvitationsAsync(token));
            }
            catch (ParticipantRequestException error)when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                ApplyFailure(error);
                denied = "現在の権限では保存できません。未保存の入力は残しています。";
            }
        }
        else
            denied ??= "現在の権限では保存できません。未保存の入力は残しています。";
        if (denied is not null)
            accessDenied = editorDenied = true;
        if (HasEditor)
            EditorError = denied ?? "未保存の入力と元の更新版を復元しました。内容を確認して保存してください。";
        else
            InvitationNotice = "未完了の発行要求を復元しました。再試行には同じ要求IDを使います。";
        NotifyState();
    }

    private void NotifyState()
    {
        foreach (var property in new[]
        {
            nameof(CanManage),
            nameof(HasEditor),
            nameof(HasLatest),
            nameof(IsSelf),
            nameof(CanSave),
            nameof(CanCancelInvitation),
            nameof(CanRestoreAuthentication),
            nameof(HasInvitationDraft),
            nameof(EmptyInvitations),
            nameof(Title),
            nameof(PrimaryText),
            nameof(Name),
            nameof(OwnRole),
            nameof(ExcludeNotice),
            nameof(LatestText),
            nameof(CancelTarget)
        }

        )
            OnPropertyChanged(property);
        EditCommand.NotifyCanExecuteChanged();
        ExcludeCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        OpenInvitationsCommand.NotifyCanExecuteChanged();
        IssueCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        Context.PropertyChanged -= ContextChanged;
        EditorRequested = SaveRequested = null;
    }
}
