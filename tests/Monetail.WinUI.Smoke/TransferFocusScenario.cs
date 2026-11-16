using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckTransferFocus()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true }; var masters = new PaymentServer(); var server = new TransferInputServer(masters);
        for (var i = 0; i < 301; i++) { var row = server.Add("先行" + i); server.Rows[row.TransferId] = row with { TransactionAt = DateTime.Today.AddHours(13) }; }
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenTransferEditorAsync(null); await FillTransfer(root, masters);
        Find<TimeInputBox>(root, "TransferTime").Text = "00:00:00";
        await InvokeAsync(() => Find<Button>(root, "TransferSave"));
        await Wait(() => Find<ContentDialog>(root, "TransferConfirmationDialog").IsLoaded);
        Check(Find<ContentDialog>(root, "TransferConfirmationDialog").Content.ToString()!.Contains("00:00:00") && server.Writes.Count == 0, "0時の新規保存は警告して確認前には送信しない");
        await ConfirmTransfer(root, ContentDialogResult.None); Check(TransferEditing is not null && server.Writes.Count == 0, "0時警告の取消は入力を保持");
        await InvokeAsync(() => Find<Button>(root, "TransferSave")); await ConfirmTransfer(root, ContentDialogResult.Primary);
        await Wait(() => TransferEditing is null && Find<Button>(root, "TransferReturnToStart").Visibility == Visibility.Visible);
        var list = Find<ListView>(root, "TransferList");
        Check(list.Items.Count == 1 && ((TransferRow)list.SelectedItem).Transfer.Name == "ATM" && server.Reads.Any(u => u.Query.Contains("focusTransferId=")), "初回300件にない保存行はfocusTransferIdで先頭表示して選択");
        Check(Find<Button>(root, "TransferReturnToStart").ActualWidth > 0, "途中開始の先頭に戻るは一覧を末尾までスクロールせず使用できる");
        await InvokeAsync(() => Find<Button>(root, "TransferReturnToStart")); await Wait(() => list.Items.Count == 100 && Find<Button>(root, "TransferReturnToStart").Visibility == Visibility.Collapsed);
        Check(!server.Reads[^1].Query.Contains("focusTransferId=") && list.SelectedItem is null, "先頭に戻るはフォーカス指定と選択を解除し通常の先頭を取得");
    }
}
