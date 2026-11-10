using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;
using Windows.ApplicationModel.DataTransfer;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckParticipantsAndInvitations()
    {
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new ParticipantsServer(fixture.AccountId);
            fixture.HasPreviousLedger = true;
            fixture.ParticipantsHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
            await Settle();
            var navigation = Find<NavigationView>(root, "ManagementNavigation");
            Check(Find<NavigationViewItem>(root, "ParticipantsNavigation").Visibility == Visibility.Visible, "家計簿管理者に参加者管理の入口を表示");
            navigation.SelectedItem = Find<NavigationViewItem>(root, "ParticipantsNavigation");
            var list = Find<ListView>(root, "ParticipantsList");
            await Wait(() => list.Items.Count == 2);
            Check(((ParticipantRow)list.Items[0]).Member.AccountId == server.Other.AccountId && ((ParticipantRow)list.Items[1]).Member.AccountId == fixture.AccountId,
                "WinUIは合成APIの配列順を維持し、本人先頭などの独自ソートをしない");
            await Settle();
            Check(All(list).OfType<TextBlock>().All(text => !text.Text.Contains(fixture.AccountId.ToString(), StringComparison.OrdinalIgnoreCase)), "一覧の同名アカウントに内部IDを表示しない");
            var own = (ParticipantRow)list.Items[1];
            Check(own.ExcludeVisibility == Visibility.Collapsed, "本人の除外ボタンを表示しない");
            await InvokeAsync(() => ParticipantActionButton(list, fixture.AccountId, "編集"));
            await Wait(() => Find<ScrollViewer>(root, "ParticipantEditorContent").Visibility == Visibility.Visible);
            await Settle();
            Check(Find<ComboBox>(root, "ParticipantRole").Visibility == Visibility.Collapsed && Find<TextBlock>(root, "ParticipantOwnRole").Text.Contains("管理者"), "本人の役割は読み取り専用");
            await InvokeAsync(() => Find<Button>(root, "ReturnButton"));
            Check(Find<Grid>(root, "ManagementPage").Visibility == Visibility.Visible, "参加者ダイアログ中は背後の遷移を抑止");
            Find<ComboBox>(root, "ParticipantPerson").SelectedIndex = 0;
            await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", true));
            await Wait(() => ParticipantEditorClosed(root));
            Check(server.OwnPersonCleared, "本人の人物対応だけは編集・解除でき、役割は送らない");

            await InvokeAsync(() => ParticipantActionButton(list, server.Other.AccountId, "編集"));
            await Wait(() => !ParticipantEditorClosed(root)); await Settle();
            var role = Find<ComboBox>(root, "ParticipantRole"); var person = Find<ComboBox>(root, "ParticipantPerson");
            Check(server.EditReads == 2 && person.Items.OfType<ComboBoxItem>().Any(item => !item.IsEnabled), "編集開始の1要求で人物候補を取得し対応済み候補を無効化");
            role.SelectedIndex = 2; person.SelectedIndex = 0;
            server.Conflict = true;
            var reads = server.ListReads;
            await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", true));
            await Wait(() => Find<Border>(root, "ParticipantConflict").Visibility == Visibility.Visible);
            Check(role.SelectedIndex == 2 && person.SelectedIndex == 0 && !ParticipantDialogAction(root, "ParticipantDialog", true).IsEnabled && server.ListReads == reads,
                "更新版競合は未保存入力と元の版を保持し、追加GETや自動上書きをしない");
            Check(Find<TextBlock>(root, "LatestParticipant").Text.Contains("編集者") && Find<TextBlock>(root, "LatestParticipant").Text.Contains("未使用の人物"), "競合の最新役割・人物・候補を読み取り専用で表示");
            await InvokeAsync(() => Find<Button>(root, "UseLatestParticipantButton"));
            Check(role.SelectedIndex == 1 && person.SelectedIndex == 2, "明示的な最新読込だけで入力と更新版を切替");
            var latestRevision = server.Other.Revision;
            await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", true));
            await Wait(() => ParticipantEditorClosed(root));
            Check(server.LastRevision == latestRevision && server.ListReads == reads && list.Items.Count == 2, "保存の成功一覧を使い追加GETなしで戻る");

            await InvokeAsync(() => ParticipantActionButton(list, server.Other.AccountId, "編集"));
            await Wait(() => !ParticipantEditorClosed(root)); await Settle();
            role.SelectedIndex = 2; person.SelectedIndex = 0;
            server.AssignConflict = true;
            await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", true));
            await Wait(() => Find<TextBlock>(root, "ParticipantPersonError").Visibility == Visibility.Visible);
            Check(role.SelectedIndex == 2 && person.SelectedIndex == 0, "人物対応の重複は欄エラーで入力保持");
            server.FailWrite = true;
            await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", true));
            await Wait(() => Find<TextBlock>(root, "ParticipantError").Visibility == Visibility.Visible);
            Check(role.SelectedIndex == 2 && !ParticipantEditorClosed(root), "通信失敗はダイアログに表示し入力を保持");
            server.FailWrite = false;
            await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", false)); await Wait(() => ParticipantEditorClosed(root));

            await InvokeAsync(() => ParticipantActionButton(list, server.Other.AccountId, "除外"));
            await Wait(() => !ParticipantEditorClosed(root)); await Settle();
            Check(Find<ContentDialog>(root, "ParticipantDialog").DefaultButton == ContentDialogButton.Close && server.Exclusions == 0, "除外は対象名を確認しキャンセルを既定にする");
            reads = server.ListReads;
            await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", true)); await Wait(() => ParticipantEditorClosed(root));
            Check(list.Items.Count == 1 && server.Exclusions == 1 && server.ListReads == reads, "除外後は応答の一覧を反映し追加GETしない");

            await InvokeAsync(() => Find<Button>(root, "OpenInvitationsButton"));
            var invitations = Find<ListView>(root, "InvitationsList");
            await Wait(() => invitations.Items.Count == 2);
            await Settle();
            Check(Find<Grid>(root, "InvitationsContent").Visibility == Visibility.Visible && Find<Grid>(root, "ParticipantsContent").Visibility == Visibility.Collapsed,
                "招待は別Windowやダイアログではなく右ペインの専用画面");
            Check(((LedgerInvitationRow)invitations.Items[0]).Invitation.CreatedAtUtc > ((LedgerInvitationRow)invitations.Items[1]).Invitation.CreatedAtUtc, "招待は新しい発行を上に表示");
            var newest = ((LedgerInvitationRow)invitations.Items[0]).Invitation;
            await InvokeInvitationActionButton(invitations, newest.InvitationId, "コピー");
            Check(await Clipboard.GetContent().GetTextAsync() == newest.Url, "既存招待URLを明示操作でクリップボードにコピー");
            var inviteReads = server.InvitationReads;
            server.LoseCreation = true;
            await InvokeAsync(() => Find<Button>(root, "IssueInvitationButton"));
            await Wait(() => Find<TextBlock>(root, "InvitationRetryNotice").Visibility == Visibility.Visible);
            Check(invitations.Items.Count == 2 && server.InvitationReads == inviteReads, "招待発行の応答喪失は自動再取得せず再試行を案内");
            await InvokeAsync(() => Find<Button>(root, "IssueInvitationButton"));
            await Wait(() => invitations.Items.Count == 3);
            Check(server.CreateRequests.Count == 2 && server.CreateRequests[0] == server.CreateRequests[1] && server.CreatedCount == 1 && server.InvitationReads == inviteReads,
                "応答喪失後の発行は同じ要求IDでリンクを重複作成せず応答一覧を使用");
            await InvokeAsync(() => Find<Button>(root, "IssueInvitationButton")); await Wait(() => invitations.Items.Count == 4);
            Check(server.CreateRequests[2] != server.CreateRequests[1], "次の新規招待発行は新しい要求ID");
            newest = ((LedgerInvitationRow)invitations.Items[0]).Invitation;
            await InvokeInvitationActionButton(invitations, newest.InvitationId, "取消");
            await Wait(() => Find<StackPanel>(root, "InvitationCancelContent").Visibility == Visibility.Visible); await Settle();
            Check(server.CancelCount == 0, "招待取消は確認するまでAPIを呼ばない");
            await InvokeAsync(() => ParticipantDialogAction(root, "InvitationCancelDialog", true));
            await Wait(() => Find<StackPanel>(root, "InvitationCancelContent").Visibility == Visibility.Collapsed);
            Check(invitations.Items.Count == 3 && server.CancelCount == 1 && server.InvitationReads == inviteReads, "招待取消は成功応答の一覧を反映");
            await InvokeAsync(() => Find<Button>(root, "BackToParticipantsButton")); await Wait(() => Find<Grid>(root, "ParticipantsContent").Visibility == Visibility.Visible);
        }
        // 権限拒否は再取得せず、閉じた後の参加者管理への入口を隠す。
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new ParticipantsServer(fixture.AccountId); fixture.HasPreviousLedger = true; fixture.ParticipantsHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await Settle();
            Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "ParticipantsNavigation");
            var list = Find<ListView>(root, "ParticipantsList"); await Wait(() => list.Items.Count == 2); await Settle();
            await InvokeAsync(() => ParticipantActionButton(list, server.Other.AccountId, "編集")); await Wait(() => !ParticipantEditorClosed(root)); await Settle();
            Find<ComboBox>(root, "ParticipantRole").SelectedIndex = 1; server.Deny = true; var reads = server.ListReads;
            await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", true));
            await Wait(() => Find<TextBlock>(root, "ParticipantError").Visibility == Visibility.Visible);
            Check(!ParticipantDialogAction(root, "ParticipantDialog", true).IsEnabled && Find<ComboBox>(root, "ParticipantRole").SelectedIndex == 1 && server.ListReads == reads,
                "降格の403は入力を保持し、応答の権限だけで操作を止め再取得しない");
            await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", false)); await Wait(() => ParticipantEditorClosed(root));
            Check(Find<NavigationViewItem>(root, "ParticipantsNavigation").Visibility == Visibility.Collapsed && Find<Grid>(root, "HomePage").Visibility == Visibility.Visible,
                "権限喪失後は管理画面から閉じて再入場不可");
        }
    }
    private async Task CheckParticipantAuthenticationRecovery(bool includeInvitation = true)
    {
        foreach (var sameAccount in new[] { true, false })
        {
            using var fixture = new SyntheticConnection(saved: true);
            var server = new ParticipantsServer(fixture.AccountId); fixture.HasPreviousLedger = true; fixture.ParticipantsHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await Settle();
            Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "ParticipantsNavigation");
            var list = Find<ListView>(root, "ParticipantsList"); await Wait(() => list.Items.Count == 2); await Settle();
            await InvokeAsync(() => ParticipantActionButton(list, server.Other.AccountId, "編集")); await Wait(() => !ParticipantEditorClosed(root)); await Settle();
            Find<ComboBox>(root, "ParticipantRole").SelectedIndex = 2;
            var revision = server.Other.Revision;
            fixture.RejectSession = true;
            await WaitParticipantDialogReady(root);
            var originalButton = ParticipantDialogAction(root, "ParticipantDialog", true);
            await InvokeAsync(() => originalButton);
            await Wait(() => Visible(root, "LoginPage") && ParticipantEditorClosed(root));
            await Wait(() => !InvokeTargetReady(originalButton));
            var rejected = false;
            try { Invoke(originalButton); }
            catch (InvocationRejectedException error) { rejected = error.Reason == "not_ready"; }
            Check(rejected, "閉鎖済みダイアログのButton参照はXamlRootが残っていてもPeerへ渡さない");
            if (!sameAccount) fixture.AccountId = Guid.NewGuid();
            fixture.RejectSession = false; fixture.CompleteLogin = true;
            await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            if (sameAccount)
            {
                await Wait(() => !ParticipantEditorClosed(root)); await Settle();
                Check(Find<ComboBox>(root, "ParticipantRole").SelectedIndex == 2 && server.LastRevision == Guid.Empty,
                    "同じ本人の再ログインで参加者の未保存入力を復元し自動保存しない");
                server.Conflict = true;
                await WaitParticipantDialogReady(root);
                await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", true));
                await Wait(() => Find<Border>(root, "ParticipantConflict").Visibility == Visibility.Visible);
                Check(server.LastRevision == revision && Find<ComboBox>(root, "ParticipantRole").SelectedIndex == 2,
                    "参加者編集の認証復帰でも元の更新版を維持して他者の変更を上書きしない");
                await InvokeAsync(() => ParticipantDialogAction(root, "ParticipantDialog", false)); await Wait(() => ParticipantEditorClosed(root));
            }
            else
            {
                await Wait(() => Visible(root, "HomePage"));
                Check(ParticipantEditorClosed(root) && server.EditReads == 1, "別アカウントへ参加者の入力を引き継がない");
            }
            await fixture.CallbackTask!;
        }
        if (!includeInvitation) return;
        using (var fixture = new SyntheticConnection(saved: true))
        {
            var server = new ParticipantsServer(fixture.AccountId); fixture.HasPreviousLedger = true; fixture.ParticipantsHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await Settle();
            Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "ParticipantsNavigation");
            await Wait(() => Find<ListView>(root, "ParticipantsList").Items.Count == 2);
            await InvokeAsync(() => Find<Button>(root, "OpenInvitationsButton")); await Wait(() => Find<ListView>(root, "InvitationsList").Items.Count == 2);
            fixture.RejectSession = true;
            await InvokeAsync(() => Find<Button>(root, "IssueInvitationButton")); await Wait(() => Visible(root, "LoginPage"));
            var original = JsonDocument.Parse(fixture.RejectedManagementBodies.Single()).RootElement.GetProperty("requestId").GetGuid();
            fixture.RejectSession = false; fixture.CompleteLogin = true; await InvokeAsync(() => Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "ManagementPage") && Find<Grid>(root, "InvitationsContent").Visibility == Visibility.Visible);
            await Settle(); await fixture.CallbackTask!;
            Check(server.CreatedCount == 0 && Find<TextBlock>(root, "InvitationRetryNotice").Visibility == Visibility.Visible,
                "招待発行の認証復帰は未完了要求を復元し自動発行しない");
            await InvokeAsync(() => Find<Button>(root, "IssueInvitationButton")); await Wait(() => server.CreatedCount == 1);
            Check(server.CreateRequests.Single() == original, "招待発行の認証復帰でも同じrequestIdを再使用");
        }
    }
    private static bool ParticipantEditorClosed(FrameworkElement root) => Find<ScrollViewer>(root, "ParticipantEditorContent").Visibility == Visibility.Collapsed;
    private static async Task WaitParticipantDialogReady(FrameworkElement root) => await Wait(() => !ParticipantEditorClosed(root) &&
        Find<ContentDialog>(root, "ParticipantDialog").IsLoaded && Find<ContentControl>(root, "Operations").IsEnabled &&
        All(Find<ContentDialog>(root, "ParticipantDialog")).OfType<Button>().Any(b => InvokeTargetReady(b) &&
            Equals(b.Content, Find<ContentDialog>(root, "ParticipantDialog").PrimaryButtonText)));
    private static Button ParticipantDialogAction(FrameworkElement root, string name, bool primary)
    {
        var dialog = Find<ContentDialog>(root, name);
        var text = primary ? dialog.PrimaryButtonText : dialog.CloseButtonText;
        var candidates = All(dialog).OfType<Button>().Where(button => Equals(button.Content, text)).ToArray();
        // 旧First選択との差を観測し、内容や認証データを出力しない。
        // 読み取り用途では無効な標準ボタンも返す。Invokeの可否は共通ヘルパーで検証する。
        var selected = candidates.OrderByDescending(InvokeTargetReady).ThenByDescending(button => button.IsLoaded).First();
        TraceInvocation($"participantButtonCandidates={candidates.Length}; first={TargetState(candidates[0])}; selected={TargetState(selected)}");
        return selected;
    }
    private static Button ParticipantActionButton(ListView list, Guid accountId, string label) =>
        All(list).OfType<Button>().First(button => button.Tag is MemberInfo member && member.AccountId == accountId && Equals(button.Content, label));
    private static async Task InvokeInvitationActionButton(ListView list, Guid id, string label)
    {
        list.ScrollIntoView(list.Items.Cast<LedgerInvitationRow>().First(row => row.Invitation.InvitationId == id));
        await Wait(() => All(list).OfType<Button>().Any(button => button.Tag is LedgerInvitationInfo invite && invite.InvitationId == id && Equals(button.Content, label) && InvokeTargetReady(button)));
        await InvokeAsync(() => All(list).OfType<Button>().First(button => button.Tag is LedgerInvitationInfo invite && invite.InvitationId == id && Equals(button.Content, label) && InvokeTargetReady(button)));
    }

    private sealed class ParticipantsServer
    {
        internal readonly Guid Account;
        internal MemberInfo Other;
        private MemberInfo own;
        internal bool OwnPersonCleared => own.PersonId is null;
        private readonly Guid occupied = Guid.NewGuid(), free = Guid.NewGuid();
        private bool excluded;
        internal int ListReads, EditReads, InvitationReads, Exclusions, CreatedCount, CancelCount;
        internal bool Conflict, AssignConflict, FailWrite, Deny, LoseCreation;
        internal Guid LastRevision;
        internal readonly List<Guid> CreateRequests = [];
        private readonly Dictionary<Guid, LedgerInvitationInfo> results = [];
        private readonly List<LedgerInvitationInfo> invitations = [];
        internal ParticipantsServer(Guid account)
        {
            Account = account;
            own = new(account, "同じ名前", "Admin", occupied, "父", 0, DateTimeOffset.UtcNow, Guid.NewGuid());
            Other = new(Guid.NewGuid(), "同じ名前", "Viewer", null, null, null, DateTimeOffset.UtcNow.AddDays(-1), Guid.NewGuid());
            invitations.Add(NewInvitation(DateTimeOffset.UtcNow.AddDays(-2)));
            invitations.Add(NewInvitation(DateTimeOffset.UtcNow.AddDays(-1)));
        }
        private MemberPersonCandidate[] Candidates(Guid id) => [new(occupied, "父", id == Account), new(free, "未使用の人物", true)];
        private MembersInfo Members => new(excluded ? [own] : [Other, own], Account, "Admin");
        private static LedgerInvitationInfo NewInvitation(DateTimeOffset created) => new(Guid.NewGuid(), SyntheticConnection.Endpoint.Value + "ledger-invitation#token=" + new string('S', 43), created, created.AddDays(7));
        internal async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/members", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get)
                {
                    if (path.EndsWith("/edit", StringComparison.Ordinal))
                    {
                        EditReads++; var self = path.Contains(Account.ToString("N"), StringComparison.Ordinal);
                        return Json(new MemberEditInfo(self ? own : Other, Candidates(self ? Account : Other.AccountId)));
                    }
                    ListReads++; return Json(Members);
                }
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                LastRevision = body.RootElement.GetProperty("revision").GetGuid();
                if (Deny) return Json(new { code = "forbidden", permissions = new { currentRole = "Viewer", myPersonId = (Guid?)null } }, HttpStatusCode.Forbidden);
                if (FailWrite) return new(HttpStatusCode.ServiceUnavailable);
                if (Conflict)
                {
                    Conflict = false; Other = Other with { Role = "Editor", PersonId = free, PersonName = "未使用の人物", PersonDisplayOrder = 1, Revision = Guid.NewGuid() };
                    return Json(new { code = "revision_conflict", latest = new MemberEditInfo(Other, Candidates(Other.AccountId)) }, HttpStatusCode.Conflict);
                }
                if (AssignConflict) { AssignConflict = false; return Json(new { code = "person_already_assigned", field = "personId" }, HttpStatusCode.Conflict); }
                if (request.Method == HttpMethod.Delete) { excluded = true; Exclusions++; }
                else
                {
                    var person = body.RootElement.GetProperty("personId");
                    if (path.EndsWith(Account.ToString("N"), StringComparison.Ordinal))
                    {
                        if (body.RootElement.TryGetProperty("role", out _)) throw new InvalidOperationException("本人の役割は送信しません。");
                        own = own with { PersonId = null, PersonName = null, PersonDisplayOrder = null, Revision = Guid.NewGuid() };
                        return Json(Members);
                    }
                    Other = Other with { Role = body.RootElement.GetProperty("role").GetString()!, PersonId = person.ValueKind == JsonValueKind.Null ? null : person.GetGuid(),
                        PersonName = person.ValueKind == JsonValueKind.Null ? null : "未使用の人物", PersonDisplayOrder = person.ValueKind == JsonValueKind.Null ? null : 1, Revision = Guid.NewGuid() };
                }
                return Json(Members);
            }
            if (request.Method == HttpMethod.Get) { InvitationReads++; return Json(new LedgerInvitationsInfo(invitations.ToArray())); }
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var id = body.RootElement.GetProperty("requestId").GetGuid(); CreateRequests.Add(id);
                if (!results.TryGetValue(id, out var created)) { created = NewInvitation(DateTimeOffset.UtcNow); results.Add(id, created); invitations.Add(created); CreatedCount++; }
                if (LoseCreation) { LoseCreation = false; return new(HttpStatusCode.ServiceUnavailable); }
                return Json(new { invitation = created, invitations = invitations.ToArray() });
            }
            var inviteId = Guid.Parse(path.Split('/').Last()); invitations.RemoveAll(invite => invite.InvitationId == inviteId); CancelCount++;
            return Json(new LedgerInvitationsInfo(invitations.ToArray()));
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    }
}
