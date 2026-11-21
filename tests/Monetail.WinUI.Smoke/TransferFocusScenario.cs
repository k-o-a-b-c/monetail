using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckTransferFocus()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true }; var masters = new PaymentServer(); var server = new TransferInputServer(masters);
        for (var i = 0; i < 301; i++) { var row = server.Add("先行" + i); server.Rows[row.TransferId] = row with { TransactionAt = DateTime.SpecifyKind(DateTime.Today.AddHours(13), DateTimeKind.Unspecified) }; }
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenTransferEditorAsync(null); await FillTransfer(root, masters);
        Find<TimeInputBox>(root, "TransferTime").Text = "00:00:00";
        await InvokeAsync(() => Find<Button>(root, "TransferSave"));
        await Wait(() => Find<ContentDialog>(root, "TransferConfirmationDialog").IsLoaded);
        Check(Find<ContentDialog>(root, "TransferConfirmationDialog").Content.ToString()!.Contains("00:00:00") && server.Writes.Count == 0, "0時の新規保存は警告して確認前には送信しない");
        await ConfirmTransfer(root, ContentDialogResult.None); Check(TransferEditing is not null && server.Writes.Count == 0, "0時警告の取消は入力を保持");
        await InvokeAsync(() => Find<Button>(root, "TransferSave")); await ConfirmTransfer(root, ContentDialogResult.Primary);
        await Wait(() => TransferEditing is null && Find<Button>(root, "TransferReturnToStart").Visibility == Visibility.Visible);
        // Visibilityの切替とレイアウト確定は別ターン。配置後の実寸で操作可能性を検証する。
        await Wait(() => Find<Button>(root, "TransferReturnToStart") is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 });
        var list = Find<ListView>(root, "TransferList");
        Check(list.Items.Count == 1 && ((Monetail.Windows.Presentation.Transfers.TransferListItem)list.SelectedItem).Transfer.Name == "ATM" && server.Reads.Any(u => u.Query.Contains("focusTransferId=")), "初回300件にない保存行はfocusTransferIdで先頭表示して選択");
        Check(Find<Button>(root, "TransferReturnToStart").ActualWidth > 0, "途中開始の先頭に戻るは一覧を末尾までスクロールせず使用できる");
        await InvokeAsync(() => Find<Button>(root, "TransferReturnToStart")); await Wait(() => list.Items.Count == 100 && Find<Button>(root, "TransferReturnToStart").Visibility == Visibility.Collapsed);
        Check(!server.Reads[^1].Query.Contains("focusTransferId=") && list.SelectedItem is null, "先頭に戻るはフォーカス指定と選択を解除し通常の先頭を取得");
    }
    private async Task CheckTransferOpening()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true }; var masters = new PaymentServer(); var server = new TransferInputServer(masters);
        var row = server.Add(); server.DetailGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
        var opening = window!.OpenTransferEditorAsync(row.TransferId); await Wait(() => server.Details == 1 && Find<ProgressRing>(root, "TransferOpenProgress").IsActive);
        await window.OpenTransferEditorAsync(row.TransferId); Check(server.Details == 1, "振替詳細GET中の二重起動を拒否");
        var openingProgress = Find<ProgressRing>(root, "TransferOpenProgress");
        await InvokeAsync(() => Find<Button>(root, "SettingsButton")); await opening; await Wait(() => Visible(root, "ManagementPage"));
        Check(!openingProgress.IsActive && TransferEditing is null, "詳細GET中の画面離脱は要求を中断し編集を生成しない");
        server.DetailGate.SetResult(true); await InvokeAsync(() => Find<Button>(root, "ReturnButton")); await Wait(() => Visible(root, "HomePage"));
        Check(Find<Button>(root, "TransferRegisterPlaceholder").IsEnabled && TransferEditing is null, "詳細GETの中断後も登録入口を復元し旧応答を適用しない");
        server.DetailGate = null; server.Rows.Remove(row.TransferId); await window.OpenTransferEditorAsync(row.TransferId);
        Check(TransferEditing is null && Find<TextBlock>(root, "TransferOpenMessage").Text.Contains("アクセス") && !Find<ProgressRing>(root, "TransferOpenProgress").IsActive, "詳細404は一覧を保持して失効と不存在を断定せず表示");
    }
}
