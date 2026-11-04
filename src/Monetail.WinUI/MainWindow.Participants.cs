using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Windows.ApplicationModel.DataTransfer;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private MembersInfo? membersInfo;
    private MemberEditInfo? memberEdit;
    private MemberEditInfo? latestMemberEdit;
    private LedgerInvitationsInfo? invitationsInfo;
    private LedgerInvitationDraft? invitationDraft;
    private LedgerInvitationInfo? cancellingInvitation;
    private bool excludingMember, memberDenied, membersAccessDenied;
    private bool participantDialogOpen, invitationDialogOpen, forceParticipantClose;
    private Task participantDialogTask = Task.CompletedTask;
    private ParticipantEditSnapshot? pendingParticipantEdit;
    private sealed record ParticipantEditSnapshot(Guid AccountId, string Origin, LedgerInfo Ledger, MembersInfo? Members,
        MemberEditInfo? Edit, MemberEditInfo? Latest, string Role, Guid? PersonId, LedgerInvitationDraft? Invitation);
    private bool ManagementDialogOpen => personDialogOpen || participantDialogOpen || invitationDialogOpen;
    private bool CanManageParticipants => account is not null && currentLedger is not null && !membersAccessDenied &&
        (peoplePermissions is null ? currentLedger.Role : peoplePermissions.CurrentRole) == "Admin";
    private ParticipantsWorkspace Participants => new(connection.Api, connection.Session, currentLedger!.LedgerId);

    private void ResetParticipants()
    {
        forceParticipantClose = true;
        if (participantDialogOpen) ParticipantDialog.Hide();
        if (invitationDialogOpen) InvitationCancelDialog.Hide();
        membersInfo = null; memberEdit = latestMemberEdit = null;
        invitationsInfo = null; invitationDraft = null; cancellingInvitation = null;
        membersAccessDenied = memberDenied = false;
        ParticipantsList.ItemsSource = InvitationsList.ItemsSource = null;
        ParticipantsContent.Visibility = InvitationsContent.Visibility = Visibility.Collapsed;
        UpdateParticipantActions();
    }

    private void UpdateParticipantActions()
    {
        ParticipantsNavigation.Visibility = CanManageParticipants ? Visibility.Visible : Visibility.Collapsed;
        OpenInvitationsButton.IsEnabled = IssueInvitationButton.IsEnabled = !busy && CanManageParticipants;
        ParticipantDialog.IsPrimaryButtonEnabled = !busy && CanManageParticipants && !memberDenied && latestMemberEdit is null && memberEdit is not null;
        InvitationCancelDialog.IsPrimaryButtonEnabled = !busy && CanManageParticipants && cancellingInvitation is not null;
    }

    private void DisplayMembers(MembersInfo info)
    {
        membersInfo = info;
        peoplePermissions = new(info.CurrentRole, info.Members.FirstOrDefault(row => row.AccountId == info.CurrentAccountId)?.PersonId);
        ParticipantsList.ItemsSource = info.Members.Select(member => new ParticipantRow(member, member.AccountId == info.CurrentAccountId)).ToArray();
        RenderPeopleRows();
        UpdatePeopleActions();
    }

    private Task LoadParticipantsAsync() => !CanManageParticipants ? Task.CompletedTask : RunAsync(async () =>
    {
        try { DisplayMembers(await Participants.ListAsync(lifetime.Token)); }
        catch (ParticipantRequestException error) { ApplyParticipantFailure(error); ShowError(ParticipantMessage(error)); }
    }, LoadParticipantsAsync);

    private async void EditParticipant(object sender, RoutedEventArgs args)
    {
        if (busy || ManagementDialogOpen || !CanManageParticipants || sender is not Button { Tag: MemberInfo member }) return;
        await RunAsync(async () =>
        {
            try { BeginParticipantDialog(await Participants.EditAsync(member.AccountId, lifetime.Token), false); }
            catch (ParticipantRequestException error) { ApplyParticipantFailure(error); ShowError(ParticipantMessage(error)); }
        }, () => Task.CompletedTask);
    }
    private void ExcludeParticipant(object sender, RoutedEventArgs args)
    {
        if (busy || ManagementDialogOpen || !CanManageParticipants || sender is not Button { Tag: MemberInfo member } || member.AccountId == account!.AuthUserId) return;
        BeginParticipantDialog(new(member, []), true);
    }
    private void BeginParticipantDialog(MemberEditInfo edit, bool excluding)
    {
        memberEdit = edit; latestMemberEdit = null; excludingMember = excluding; memberDenied = false;
        ParticipantDialog.Title = excluding ? "参加者の除外" : "参加者の編集";
        ParticipantDialog.PrimaryButtonText = excluding ? "除外する" : "保存";
        ParticipantDialog.DefaultButton = excluding ? ContentDialogButton.Close : ContentDialogButton.None;
        ParticipantFields.Visibility = excluding ? Visibility.Collapsed : Visibility.Visible;
        ParticipantExcludeNotice.Visibility = excluding ? Visibility.Visible : Visibility.Collapsed;
        ParticipantConflict.Visibility = ParticipantError.Visibility = ParticipantPersonError.Visibility = Visibility.Collapsed;
        FillParticipantFields(edit);
        ParticipantEditorContent.Visibility = Visibility.Visible;
        UpdateParticipantActions();
        participantDialogTask = ShowParticipantDialogAsync();
    }
    private void FillParticipantFields(MemberEditInfo edit)
    {
        var member = edit.Member;
        ParticipantName.Text = member.DisplayName ?? "表示名未設定";
        ParticipantExcludeNotice.Text = $"「{member.DisplayName ?? "表示名未設定"}」をこの家計簿から除外しますか。アカウント・人物・過去の記録は削除しません。";
        var self = member.AccountId == account?.AuthUserId;
        ParticipantRole.Visibility = self ? Visibility.Collapsed : Visibility.Visible;
        ParticipantOwnRole.Visibility = self ? Visibility.Visible : Visibility.Collapsed;
        ParticipantOwnRole.Text = $"家計簿内の役割：{ParticipantRow.RoleName(member.Role)}（本人の役割は変更できません）";
        ParticipantRole.SelectedItem = ParticipantRole.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == member.Role);
        ParticipantPerson.Items.Clear();
        ParticipantPerson.Items.Add(new ComboBoxItem { Content = "未設定" });
        foreach (var candidate in edit.PersonCandidates)
            ParticipantPerson.Items.Add(new ComboBoxItem { Content = candidate.Name, Tag = candidate.PersonId, IsEnabled = candidate.IsSelectable });
        ParticipantPerson.SelectedItem = ParticipantPerson.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, member.PersonId));
    }
    private async Task ShowParticipantDialogAsync()
    {
        participantDialogOpen = true; forceParticipantClose = false;
        ParticipantDialog.XamlRoot = WorkspaceRoot.XamlRoot;
        try { await ParticipantDialog.ShowAsync(); }
        finally
        {
            participantDialogOpen = false;
            if (!closed)
            {
                ParticipantEditorContent.Visibility = Visibility.Collapsed;
                memberEdit = latestMemberEdit = null;
                if (!forceParticipantClose && !CanManageParticipants) ShowPage(HomePage);
                UpdateParticipantActions();
            }
        }
    }
    private void ParticipantDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (busy && !forceParticipantClose && !closed) args.Cancel = true;
    }
    private async void SaveParticipant(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try { args.Cancel = !await TrySaveParticipantAsync(); }
        finally { deferral.Complete(); }
    }
    private async Task<bool> TrySaveParticipantAsync()
    {
        if (busy || !ParticipantDialog.IsPrimaryButtonEnabled || memberEdit is null) return false;
        var member = memberEdit.Member;
        var role = member.AccountId == account!.AuthUserId ? member.Role : (ParticipantRole.SelectedItem as ComboBoxItem)?.Tag as string;
        if (!excludingMember && (role is null || ParticipantPerson.SelectedItem is not ComboBoxItem { IsEnabled: true })) return false;
        var person = (ParticipantPerson.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        var saved = false;
        ParticipantError.Visibility = ParticipantPersonError.Visibility = Visibility.Collapsed;
        await RunAsync(async () =>
        {
            try
            {
                var result = excludingMember ? await Participants.ExcludeAsync(member, account.AuthUserId, lifetime.Token)
                    : await Participants.SaveAsync(member, role!, person, account.AuthUserId, lifetime.Token);
                DisplayMembers(result); saved = true;
            }
            catch (ParticipantRequestException error)
            {
                if (error.Latest is { } latest)
                {
                    if (latest.Member.AccountId != member.AccountId) { ShowParticipantError("応答を確認できませんでした。入力は保持しています。"); return; }
                    ShowLatestParticipant(latest);
                }
                else
                {
                    ApplyParticipantFailure(error);
                    memberDenied = error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound;
                    if (error.Code == "person_already_assigned")
                    {
                        ParticipantPersonError.Text = "この人物は別の参加者に設定されています。人物を選び直してください。";
                        ParticipantPersonError.Visibility = Visibility.Visible;
                    }
                    else ShowParticipantError(ParticipantMessage(error));
                }
                UpdateParticipantActions();
            }
        }, () => Task.CompletedTask);
        return saved;
    }
    private void ShowLatestParticipant(MemberEditInfo latest)
    {
        latestMemberEdit = latest;
        LatestParticipant.Text = $"アカウント：{latest.Member.DisplayName ?? "表示名未設定"}\n役割：{ParticipantRow.RoleName(latest.Member.Role)}\n対応する人物：{latest.Member.PersonName ?? "未設定"}\n参加日時：{latest.Member.JoinedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}\n人物候補：{string.Join("、", latest.PersonCandidates.Select(p => p.Name + (p.IsSelectable ? "" : "（設定不可）")))}";
        ParticipantConflict.Visibility = Visibility.Visible;
    }
    private void UseLatestParticipant(object sender, RoutedEventArgs args)
    {
        if (busy || latestMemberEdit is not { } latest) return;
        memberEdit = latest; latestMemberEdit = null;
        FillParticipantFields(latest);
        ParticipantConflict.Visibility = ParticipantError.Visibility = ParticipantPersonError.Visibility = Visibility.Collapsed;
        UpdateParticipantActions();
    }
    private void ShowParticipantError(string message)
    {
        ParticipantError.Text = message;
        ParticipantError.Visibility = Visibility.Visible;
    }
    private void ApplyParticipantFailure(ParticipantRequestException error)
    {
        if (error.Permissions is { } permissions)
        {
            peoplePermissions = permissions;
            membersAccessDenied = permissions.CurrentRole != "Admin";
            if (permissions.CurrentRole is null) ParticipantsList.ItemsSource = InvitationsList.ItemsSource = null;
            RenderPeopleRows();
        }
        else if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) membersAccessDenied = true;
        UpdatePeopleActions();
    }
    private static string ParticipantMessage(ParticipantRequestException error) => error.StatusCode switch
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
        }
    };
    private async void OpenInvitations(object sender, RoutedEventArgs args)
    {
        if (busy || ManagementDialogOpen || !CanManageParticipants) return;
        ParticipantsContent.Visibility = Visibility.Collapsed;
        InvitationsContent.Visibility = Visibility.Visible;
        await LoadInvitationsAsync();
    }
    private Task LoadInvitationsAsync() => RunAsync(async () =>
    {
        try { DisplayInvitations(await Participants.InvitationsAsync(lifetime.Token)); }
        catch (ParticipantRequestException error) { ApplyParticipantFailure(error); ShowInvitationNotice(ParticipantMessage(error)); }
    }, () => Task.CompletedTask);
    private void DisplayInvitations(LedgerInvitationsInfo info)
    {
        invitationsInfo = info;
        RenderInvitations();
    }
    private void RenderInvitations()
    {
        var active = invitationsInfo?.Invitations.Where(i => i.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.InvitationId).Select(i => new LedgerInvitationRow(i)).ToArray() ?? [];
        InvitationsList.ItemsSource = active;
        EmptyInvitations.Visibility = active.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        InvitationRetryNotice.Visibility = invitationDraft is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private async void BackToParticipants(object sender, RoutedEventArgs args)
    {
        if (busy || ManagementDialogOpen) return;
        InvitationsContent.Visibility = Visibility.Collapsed;
        if (!CanManageParticipants) { ShowPage(HomePage); return; }
        ParticipantsContent.Visibility = Visibility.Visible;
        await LoadParticipantsAsync();
    }
    private async void IssueInvitation(object sender, RoutedEventArgs args)
    {
        if (busy || ManagementDialogOpen || !CanManageParticipants) return;
        invitationDraft ??= new();
        await RunAsync(async () =>
        {
            try
            {
                var result = await Participants.CreateInvitationAsync(invitationDraft, lifetime.Token);
                invitationDraft = null; DisplayInvitations(result); ShowInvitationNotice("招待リンクを発行しました。");
            }
            catch (ParticipantRequestException error) { ApplyParticipantFailure(error); ShowInvitationNotice(ParticipantMessage(error)); }
        }, () => Task.CompletedTask);
        InvitationRetryNotice.Visibility = invitationDraft is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void CopyInvitation(object sender, RoutedEventArgs args)
    {
        if (busy || !CanManageParticipants || sender is not Button { Tag: LedgerInvitationInfo invitation }) return;
        if (invitation.ExpiresAtUtc <= DateTimeOffset.UtcNow) { RenderInvitations(); ShowInvitationNotice("この招待リンクは期限切れです。"); return; }
        try
        {
            var data = new DataPackage(); data.SetText(invitation.Url); Clipboard.SetContent(data);
            ShowInvitationNotice("招待リンクをコピーしました。送付先を限定してください。");
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { ShowInvitationNotice("コピーできませんでした。クリップボードへのアクセスを確認してください。"); }
    }
    private async void CancelInvitation(object sender, RoutedEventArgs args)
    {
        if (busy || ManagementDialogOpen || !CanManageParticipants || sender is not Button { Tag: LedgerInvitationInfo invitation }) return;
        InvitationCancelContent.Visibility = Visibility.Visible;
        cancellingInvitation = invitation; invitationDialogOpen = true; forceParticipantClose = false;
        InvitationCancelError.Visibility = Visibility.Collapsed;
        InvitationCancelTarget.Text = $"有効期限が{invitation.ExpiresAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}の招待リンクを取り消しますか。既に参加した人は除外されません。";
        InvitationCancelDialog.XamlRoot = WorkspaceRoot.XamlRoot;
        UpdateParticipantActions();
        try { await InvitationCancelDialog.ShowAsync(); }
        finally
        {
            invitationDialogOpen = false; cancellingInvitation = null;
            InvitationCancelContent.Visibility = Visibility.Collapsed;
            if (!closed && !forceParticipantClose && !CanManageParticipants) ShowPage(HomePage);
        }
    }
    private void InvitationDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (busy && !forceParticipantClose && !closed) args.Cancel = true;
    }
    private async void ConfirmCancelInvitation(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (busy || !CanManageParticipants || cancellingInvitation is null) return;
        var deferral = args.GetDeferral(); var saved = false;
        try
        {
            await RunAsync(async () =>
            {
                try { DisplayInvitations(await Participants.CancelInvitationAsync(cancellingInvitation.InvitationId, lifetime.Token)); saved = true; }
                catch (ParticipantRequestException error)
                {
                    ApplyParticipantFailure(error); InvitationCancelError.Text = ParticipantMessage(error); InvitationCancelError.Visibility = Visibility.Visible;
                }
            }, () => Task.CompletedTask);
            args.Cancel = !saved;
        }
        finally { deferral.Complete(); }
    }
    private void ShowInvitationNotice(string message)
    {
        InvitationNotice.Text = message; InvitationNotice.Visibility = Visibility.Visible;
    }

    // 人物編集と同様、認証失効時だけ入力をメモリに退避し、同じ本人・Originで明示ログイン後に再開する。
    private void RememberParticipantEdit()
    {
        if (account is null || currentLedger is null) return;
        if (participantDialogOpen && !excludingMember && memberEdit is not null)
            pendingParticipantEdit = new(account.AuthUserId, connection.Endpoint!.Value, currentLedger, membersInfo, memberEdit, latestMemberEdit,
                (ParticipantRole.SelectedItem as ComboBoxItem)?.Tag as string ?? memberEdit.Member.Role,
                (ParticipantPerson.SelectedItem as ComboBoxItem)?.Tag as Guid?, null);
        else if (InvitationsContent.Visibility == Visibility.Visible && invitationDraft is not null)
            pendingParticipantEdit = new(account.AuthUserId, connection.Endpoint!.Value, currentLedger, membersInfo, null, null, "", null, invitationDraft);
    }
    private async Task<bool> ResumeParticipantEditAsync(CancellationToken token)
    {
        if (pendingParticipantEdit is not { } draft || account is null) return false;
        if (draft.AccountId != account.AuthUserId || draft.Origin != connection.Endpoint!.Value) { pendingParticipantEdit = null; return false; }
        await participantDialogTask;
        LedgerInfo ledger = draft.Ledger; PeoplePermissions? denied = null;
        MembersInfo? members = draft.Members;
        LedgerInvitationsInfo? invitations = null;
        try
        {
            ledger = await connection.Ledgers.OpenAsync(ledger.LedgerId, token);
            if (ledger.Role == "Admin")
            {
                var workspace = new ParticipantsWorkspace(connection.Api, connection.Session, ledger.LedgerId);
                if (draft.Edit is not null) members = await workspace.ListAsync(token);
                else invitations = await workspace.InvitationsAsync(token);
            }
            else denied = new(ledger.Role, null);
        }
        catch (LedgerRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) { denied = new(null, null); }
        catch (ParticipantRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) { denied = error.Permissions ?? new(null, null); }
        DisplayLedger(ledger); ShowPage(ManagementPage);
        ManagementNavigation.SelectedItem = ParticipantsNavigation;
        PeopleContent.Visibility = ConnectionSettingsContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
        if (members is not null) DisplayMembers(members);
        if (draft.Edit is { } edit)
        {
            ParticipantsContent.Visibility = Visibility.Visible;
            BeginParticipantDialog(edit, false);
            ParticipantRole.SelectedItem = ParticipantRole.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, draft.Role));
            ParticipantPerson.SelectedItem = ParticipantPerson.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, draft.PersonId));
            if (draft.Latest is not null) ShowLatestParticipant(draft.Latest);
            if (denied is not null) { peoplePermissions = denied; membersAccessDenied = memberDenied = true; }
            ShowParticipantError(denied is null ? "未保存の入力と元の更新版を復元しました。内容を確認して保存してください。" : "現在の権限では保存できません。未保存の入力は残しています。");
        }
        else
        {
            ParticipantsContent.Visibility = Visibility.Collapsed; InvitationsContent.Visibility = Visibility.Visible;
            invitationDraft = draft.Invitation;
            if (denied is not null) { peoplePermissions = denied; membersAccessDenied = true; }
            ShowInvitationNotice("未完了の発行要求を復元しました。再試行には同じ要求IDを使います。");
            if (invitations is not null) DisplayInvitations(invitations);
            else RenderInvitations();
        }
        pendingParticipantEdit = null; UpdatePeopleActions();
        return true;
    }
}
