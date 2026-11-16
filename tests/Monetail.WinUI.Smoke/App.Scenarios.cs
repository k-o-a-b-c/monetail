using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private double? rasterizationScale;
    private SmokeScenario[] Scenarios(string output, bool capture, bool repeatParticipantRecovery) =>
    [
        new("transfer-reauthentication", "transfer-editor", CheckTransferReauthentication),
        new("transfer-conflicts", "transfer-editor", CheckTransferConflicts),
        new("transfer-review", "transfer-editor", CheckTransferReview),
        new("transfer-editor", "transfer-editor", CheckTransferEditor),
        new("transfer-safety", "transfer-editor", CheckTransferSafety),
        new("transfer-suggestions", "transfer-editor", CheckTransferSuggestions),
        new("transfer-focus", "transfer-editor", CheckTransferFocus),
        new("transfer-opening", "transfer-editor", CheckTransferOpening),
        new("shell", "existing", () => CheckShell(output, capture)),
        new("participant-authentication-recovery", "existing", async () =>
        {
            var attempts = repeatParticipantRecovery ? 5 : 1;
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                TraceInvocation($"participantRecoveryAttempt={attempt}; phase=begin");
                await CheckParticipantAuthenticationRecovery(includeInvitation: attempt == 1);
                TraceInvocation($"participantRecoveryAttempt={attempt}; phase=passed");
            }
        }),
        new("categories", "existing", CheckCategories),
        new("people-filter", "existing", CheckPeopleFilter),
        new("connection-flow", "existing", () => CheckConnectionFlow(output, capture)),
        new("tax-rate-connection-cache", "existing", CheckTaxRateConnectionCache),
        new("people-flow", "existing", () => CheckPeopleFlow(output, capture)),
        new("person-deletion", "existing", CheckPersonDeletion),
        new("person-order", "existing", CheckPersonOrder),
        new("person-order-reauthentication", "existing", CheckPersonOrderReauthentication),
        new("person-reauthentication", "existing", CheckPersonReauthentication),
        new("participants-and-invitations", "existing", CheckParticipantsAndInvitations),
        new("input-controls", "existing", CheckInputControls),
        new("payments", "existing", CheckPayments),
        new("receipt-list", "existing", CheckReceiptList),
        new("transfer-list", "existing", CheckTransferList),
        new("transfer-navigation", "existing", CheckTransferNavigation),
        new("receipt-navigation", "existing", CheckReceiptNavigation),
        new("receipt-editor", "existing", CheckReceiptEditor),
        new("receipt-parent-selection", "existing", CheckReceiptParentSelection),
        new("receipt-editor-operations", "existing", CheckReceiptEditorOperations),
        new("receipt-editor-loading", "existing", CheckReceiptEditorLoading),
        new("receipt-details", "existing", CheckReceiptDetails),
        new("receipt-stored-details", "existing", CheckReceiptStoredDetails),
        new("receipt-memos", "existing", CheckReceiptMemos),
        new("receipt-tax-calculation", "existing", CheckReceiptTaxCalculation),
        new("receipt-tax-loading", "existing", CheckReceiptTaxLoading),
    ];

    // 初期ホーム/管理画面と操作ヘルパーの確認は同じ初期画面に依存するため、一組で実行する。
    private async Task CheckShell(string output, bool capture)
    {
        var root = (FrameworkElement)window!.Content;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var mother = new PersonInfo(Guid.NewGuid(), "母", today, null, 0, Guid.NewGuid());
        var father = new PersonInfo(Guid.NewGuid(), "父", null, null, 1, Guid.NewGuid());
        var child = new PersonInfo(Guid.NewGuid(), "子供", null, today.AddDays(-1), 2, Guid.NewGuid());
        window.DisplayWorkspace(new LedgerInfo(Guid.NewGuid(), "家族の家計簿", "Admin", Guid.NewGuid().ToString("N")),
            new PeopleInfo([mother, father, child], father.PersonId, "Admin", Guid.NewGuid()));
        await Settle();
        Check(Find<Grid>(root, "HomePage").Visibility == Visibility.Visible, "ホームの表示");
        if (capture) await Capture(root, output, "01-home");

        await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
        await Settle();
        var navigation = Find<NavigationView>(root, "ManagementNavigation");
        var people = Find<ListView>(root, "PeopleList");
        Check(Find<Grid>(root, "ManagementPage").Visibility == Visibility.Visible, "管理・設定への切替");
        Check(navigation.IsPaneOpen && ReferenceEquals(navigation.SelectedItem,
            Find<NavigationViewItem>(root, "PeopleNavigation")), "左メニューを保持");
        Check(people.Items.Count == 2, "人物の初期表示は本日有効な人物のみ");
        Find<CheckBox>(root, "IncludeInactivePeople").IsChecked = true;
        Check(people.Items.Count == 3, "期間外表示を選ぶと全員表示");
        Check(((PersonRow)people.Items[0]).Name == "父（本人）", "本人先頭");
        Check(((PersonRow)people.Items[1]).ValidFrom == today.ToString("yyyy/MM/dd") &&
            ((PersonRow)people.Items[2]).ValidTo == today.AddDays(-1).ToString("yyyy/MM/dd"), "既存Clientの日付表示");
        Check(Find<TextBlock>(root, "LedgerContext").Text == "家族の家計簿", "家計簿名を表示");
        Check(!All(root).OfType<MenuBar>().Any(), "メニューバーなし");
        var paneBounds = navigation.PaneDisplayMode;
        Check(paneBounds == NavigationViewPaneDisplayMode.Left, "左メニューと右コンテンツを同時表示");
        if (capture) await Capture(root, output, "02-management-people");

        window.AppWindow.ResizeClient(new SizeInt32(900, 620));
        await Settle();
        Check(people.ActualWidth > 0 && people.ActualHeight > 0, "リサイズ後の一覧表示");
        var settingsArea = Find<Grid>(root, "PeopleContent");
        Check(settingsArea.ActualWidth >= 400, "リサイズ後の操作領域");
        if (capture) await Capture(root, output, "03-management-compact");

        root.RequestedTheme = ElementTheme.Dark;
        await Settle();
        Check(root.ActualTheme == ElementTheme.Dark, "標準のダークテーマ");
        if (capture) await Capture(root, output, "04-management-dark");
        root.RequestedTheme = ElementTheme.Default;

        await InvokeAsync(() => Find<Button>(root, "ReturnButton"));
        await Settle();
        Check(Find<Grid>(root, "HomePage").Visibility == Visibility.Visible, "同じウィンドウでホームへ戻る");
        rasterizationScale = root.XamlRoot.RasterizationScale;
        await CheckInvocationGuards(root);
    }
}
