using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Views.Workspace;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckWorkspacePageLifetime()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var root = await OpenSyntheticWindow(fixture, (_, initial) =>
            Check(Find<Frame>(initial, "WorkspaceHost").Content is null,
                "認証前に未訪問の記録・管理Pageを生成しない"));
        await StartSyntheticAsync();
        var host = Find<Frame>(root, "WorkspaceHost");
        var first = host.Content as RecordHomePage ?? throw new InvalidOperationException("記録Pageがありません。");
        var model = first.ViewModel ?? throw new InvalidOperationException("記録VMがありません。");
        Find<TextBox>(root, "ReceiptMonth").Text = "2026/";
        Find<TextBox>(root, "RecordKeyword").Text = "入力中の検索";
        await Wait(() => model.MonthText == "2026/" && model.Keyword == "入力中の検索");
        var reads = fixture.Requests.Count(path => path.EndsWith("/receipts", StringComparison.Ordinal));
        await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
        await Wait(() => host.Content is ManagementPage && Equals(TransferRuntime("busy"), false));
        var managementPage = (ManagementPage)host.Content;
        Check(first.ViewModel is null && ((Monetail.WinUI.Views.Receipts.ReceiptListView)first.FindName("ReceiptsView")).ViewModel is null &&
            !ReferenceEquals(first, host.Content),
            "通常離脱は記録PageのVM購読・一覧接続を解除し、管理Pageへ標準遷移する");
        Check(Find<Grid>(root, "WorkspaceHeader").Visibility == Visibility.Visible &&
            Find<Border>(root, "RecordSelectionIndicator").Visibility == Visibility.Collapsed &&
            managementPage.FindName("ReturnButton") is null,
            "管理画面でも共通ヘッダーを表示し、記録の選択表示と専用戻るボタンを残さない");
        var menu = (NavigationView)managementPage.FindName("ManagementNavigation");
        Check(menu.MenuItems.OfType<NavigationViewItem>().All(item =>
                item.Content?.ToString() is not ("店舗管理" or "表示設定" or "入力時の初期値")),
            "廃止済みの設定メニューを表示しない");
        await InvokeAsync(() => Find<Button>(root, "RecordButton"));
        await Wait(() => host.Content is RecordHomePage { IsLoaded: true } && TransferRuntime("homeResume") is null);
        Check(Find<Grid>(root, "WorkspaceHeader").Visibility == Visibility.Visible &&
            Find<Border>(root, "RecordSelectionIndicator").Visibility == Visibility.Visible,
            "記録画面でも共通ヘッダーと選択表示を維持する");
        var second = (RecordHomePage)host.Content;
        var preserved = !ReferenceEquals(first, second) && ReferenceEquals(model, second.ViewModel) &&
            Find<TextBox>(root, "ReceiptMonth").Text == "2026/" && Find<TextBox>(root, "RecordKeyword").Text == "入力中の検索" &&
            fixture.Requests.Count(path => path.EndsWith("/receipts", StringComparison.Ordinal)) == reads &&
            ((ContentControl)managementPage.FindName("DetailsHost")).Content is null;
        Check(preserved,
            "再訪は新しいPageで同じ検索VMと未確定文字列を復元し、不要な再取得や管理内容参照を残さない" +
            (preserved ? "" : $"; samePage={ReferenceEquals(first, second)}, sameModel={ReferenceEquals(model, second.ViewModel)}, month={Find<TextBox>(root, "ReceiptMonth").Text}/{model.MonthText}, keyword={Find<TextBox>(root, "RecordKeyword").Text}/{model.Keyword}, reads={fixture.Requests.Count(path => path.EndsWith("/receipts", StringComparison.Ordinal))}/{reads}, detached={((ContentControl)managementPage.FindName("DetailsHost")).Content is null}"));
    }
}
