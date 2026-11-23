using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static Task WaitTransferDialog(FrameworkElement root, string title) => Wait(() =>
        EditorDialog(root, "TransferConfirmationDialog") is { IsLoaded: true } dialog && Equals(dialog.Title, title));

    private static bool TransferControlHasFocus(FrameworkElement root, Control target)
    {
        for (var current = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, target)) return true;
        return false;
    }
    private async Task CheckTransferNormalTabOrder(FrameworkElement root)
    {
        await FocusTransferControl(Find<Button>(root, "TransferBack"));
        foreach (var name in new[] { "TransferDate", "TransferTime", "TransferName", "TransferFrom", "TransferTo", "TransferAmount", "TransferMemo", "TransferSave" })
        {
            await PressReceiptKey(0x09);
            try { await Wait(() => TransferControlHasFocus(root, Find<Control>(root, name))); }
            catch (TaskCanceledException error) { throw new InvalidOperationException($"通常の振替Tab順: expected={name}; actual={(FocusManager.GetFocusedElement(root.XamlRoot) as FrameworkElement)?.Name}", error); }
        }
        Check(Find<Button>(root, "TransferLoadLatest").Visibility == Visibility.Collapsed && !TransferEditing!.HasUnsavedChanges,
            "通常編集・最新採用後のTab順は日付→時刻→振替名→元→先→金額→メモ→更新のまま、移動だけでは未変更");
    }

    private async Task CheckTransferConflicts()
    {
        foreach (var deleting in new[] { false, true })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var masters = new PaymentServer();
            var index = masters.Rows.FindIndex(p => p.PaymentMethodId == masters.ActiveId);
            masters.Rows[index] = masters.Rows[index] with { ValidFrom = null, ValidTo = null };
            var server = new TransferInputServer(masters); var saved = server.Add();
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            var editorWindow = window ?? throw new InvalidOperationException("振替入力のWindowがありません。");
            await editorWindow.OpenTransferEditorAsync(saved.TransferId);
            var editor = TransferEditing ?? throw new InvalidOperationException("振替の編集状態がありません。");
            if (!deleting) await CheckTransferNormalTabOrder(root);
            var amount = Find<TextBox>(root, "TransferAmount"); var memo = Find<TextBox>(root, "TransferMemo");
            await FocusTransferControl(amount); await Wait(() => memo.IsLoaded && memo.ActualWidth > 0);
            amount.Text = "2000"; memo.Text = "未保存のメモ";
            await Wait(() => editor.Draft.Amount == 2000 && editor.Draft.Memo == "未保存のメモ");
            var latest = saved with { TransactionAt = saved.TransactionAt.AddDays(1).AddSeconds(1), Name = "サーバーの最新振替", FromPaymentMethodId = saved.ToPaymentMethodId,
                ToPaymentMethodId = saved.FromPaymentMethodId, Amount = 3210, Memo = "サーバーのメモ", Revision = Guid.NewGuid() };
            server.Rows[saved.TransferId] = latest;
            var reads = server.Reads.Count;
            await InvokeAsync(() => Find<Button>(root, deleting ? "TransferDelete" : "TransferSave"));
            await WaitTransferDialog(root, deleting ? "振替の削除" : "更新の確認");
            await InvokeAsync(() => TransferDialogFooter(root, ContentDialogResult.Primary));
            await WaitTransferDialog(root, "振替が更新されています");
            var dialog = Find<ContentDialog>(root, "TransferConfirmationDialog");
            Check(dialog.PrimaryButtonText == "最新を読み込む" && dialog.CloseButtonText == "編集画面に戻る" && dialog.SecondaryButtonText == "" && dialog.DefaultButton == ContentDialogButton.Close,
                $"振替{(deleting ? "DELETE" : "PUT")}競合は指定の2択だけを表示し既定で編集へ戻る");
            Check(editor.Revision == saved.Revision && editor.Draft.Amount == 2000 && editor.Draft.Memo == "未保存のメモ" && editor.Latest == latest && server.Reads.Count == reads,
                "競合通知前に入力・旧版を保持し、応答latestを取得するためのGETを追加しない");
            if (deleting)
            {
                await InvokeAsync(() => TransferDialogFooter(root, ContentDialogResult.Primary));
                await WaitTransferDialog(root, "現在の入力を破棄しますか？");
                Check(editor.Revision == saved.Revision && editor.Draft.Amount == 2000, "最新を選んだだけでは破棄・版更新せず確認を待つ");
                await ConfirmTransfer(root, ContentDialogResult.None);
            }
            else await ConfirmTransfer(root, ContentDialogResult.None);
            await Wait(() => editor.IsReadOnly && Find<Button>(root, "TransferLoadLatest").IsEnabled);
            Check(amount.IsReadOnly && amount.IsEnabled && memo.IsReadOnly && Find<TextBox>(root, "TransferNameReadOnly").IsReadOnly &&
                Find<Button>(root, "TransferSave").Visibility == Visibility.Collapsed && Find<Button>(root, "TransferDelete").Visibility == Visibility.Collapsed && Find<Button>(root, "TransferReuse").Visibility == Visibility.Collapsed,
                "競合解消前はコピー可能な読み取り専用とし、更新・削除・流用登録を非表示にする");
            Check(Find<InfoBar>(root, "TransferEditorNotice").IsOpen && Find<Button>(root, "TransferLoadLatest").Visibility == Visibility.Visible && editor.Revision == saved.Revision && memo.Text == "未保存のメモ",
                "編集へ戻る・破棄確認取消の後も入力・警告・最新読込を保持する");
            await FocusTransferControl(Find<Button>(root, "TransferBack")); await PressReceiptKey(0x09);
            await Wait(() => TransferControlHasFocus(root, Find<Button>(root, "TransferLoadLatest")));
            await PressReceiptKey(0x09); await Wait(() => TransferControlHasFocus(root, Find<DateInputBox>(root, "TransferDate")));
            await PressReceiptKey(0x09, 0x10); await Wait(() => TransferControlHasFocus(root, Find<Button>(root, "TransferLoadLatest")));
            Check(Find<InfoBar>(root, "TransferEditorNotice").IsOpen && editor.Revision == saved.Revision && server.Writes.Count == 1,
                "競合後の実Tabは戻る→警告直後の最新読込→日付、Shift+Tabは最新読込へ戻り、移動だけでは採用しない");
            if (!deleting)
            {
                editorWindow.AppWindow.Resize(new SizeInt32(700, 620));
                var scroller = Find<ScrollViewer>(root, "TransferInputScroller");
                await Wait(() => scroller.ScrollableHeight > 0); scroller.ChangeView(null, scroller.ScrollableHeight, null, true);
                await Wait(() => scroller.VerticalOffset > 0);
                await FocusTransferControl(amount); amount.SelectAll(); await PressReceiptKey(0x43, 0x11); await Settle();
                try { Check(await Clipboard.GetContent().GetTextAsync() == "2000", "競合後も実Ctrl+Cで未保存金額をコピーしスクロールできる"); }
                finally { Clipboard.Clear(); }
                await PressReceiptKey(0x0D, 0x11); await Settle();
                Check(server.Writes.Count == 1 && editor.Revision == saved.Revision, "競合中のCtrl+Enterは古い版で再保存しない");
                await InvokeAsync(() => Find<Button>(root, "TransferLoadLatest")); await WaitTransferDialog(root, "現在の入力を破棄しますか？");
                await ConfirmTransfer(root, ContentDialogResult.None);
                Check(editor.IsReadOnly && editor.Draft.Amount == 2000 && editor.Latest == latest && server.Reads.Count == reads, "警告からの最新採用も取消時は未保存入力と取得済みlatestを保持");
            }
            await InvokeAsync(() => Find<Button>(root, "TransferLoadLatest")); await WaitTransferDialog(root, "現在の入力を破棄しますか？");
            await ConfirmTransfer(root, ContentDialogResult.Primary);
            await Wait(() => !editor.IsReadOnly && editor.Revision == latest.Revision);
            var snapshot = new TransferSnapshot(latest.TransactionAt, latest.Name, latest.FromPaymentMethodId, latest.ToPaymentMethodId, latest.Amount, latest.Memo);
            Check(editor.PrepareSave(TransferSaveKind.Update).Snapshot == snapshot && !editor.HasUnsavedChanges && editor.Latest is null && server.Reads.Count == reads && server.Writes.Count == 1,
                "確認後だけAdoptLatestで最新8項目を編集基準へ採用し、GET・保存・未保存変更を発生させない");
            Check(Find<AutoSuggestBox>(root, "TransferName").Text == latest.Name && amount.Text == "3210" && memo.Text == latest.Memo &&
                Find<DateInputBox>(root, "TransferDate").Text == latest.TransactionAt.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture) &&
                !Find<InfoBar>(root, "TransferEditorNotice").IsOpen && Find<Button>(root, "TransferLoadLatest").Visibility == Visibility.Collapsed && Find<Button>(root, "TransferSave").IsEnabled,
                "最新採用は表示も同期し警告を解除、通常編集と保存操作を復元する");
            if (!deleting) await CheckTransferNormalTabOrder(root);
            if (!deleting) { amount.Text = "4000"; await Wait(() => editor.Draft.Amount == 4000); }
            await InvokeAsync(() => Find<Button>(root, deleting ? "TransferDelete" : "TransferSave"));
            await ConfirmTransfer(root, ContentDialogResult.Primary); await Wait(() => TransferEditing is null);
            using var originalRequest = JsonDocument.Parse(server.Writes[0].Body);
            using var newRequest = JsonDocument.Parse(server.Writes[1].Body);
            Check(newRequest.RootElement.GetProperty("revision").GetGuid() == latest.Revision &&
                newRequest.RootElement.GetProperty("requestId").GetGuid() != originalRequest.RootElement.GetProperty("requestId").GetGuid() &&
                server.Writes[1].Method == (deleting ? HttpMethod.Delete : HttpMethod.Put) && (deleting ? !server.Rows.ContainsKey(saved.TransferId) : server.Rows[saved.TransferId].Amount == 4000),
                "最新採用後の更新・削除は新しい要求IDと最新revisionを送信し、通常保存へ復帰する");
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var masters = new PaymentServer(); var server = new TransferInputServer(masters) { WriteError = HttpStatusCode.Conflict, WriteErrorCode = "request_conflict" }; var saved = server.Add();
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            var editorWindow = window ?? throw new InvalidOperationException("振替入力のWindowがありません。"); await editorWindow.OpenTransferEditorAsync(saved.TransferId);
            await FocusTransferControl(Find<TextBox>(root, "TransferAmount")); Find<TextBox>(root, "TransferAmount").Text = "2000";
            await Wait(() => TransferEditing?.Draft.Amount == 2000);
            await InvokeAsync(() => Find<Button>(root, "TransferSave")); await ConfirmTransfer(root, ContentDialogResult.Primary);
            Check(TransferEditing is { Latest: null } && Find<TextBox>(root, "TransferAmount").IsReadOnly && Find<TextBox>(root, "TransferAmount").Text == "2000" &&
                Find<Button>(root, "TransferLoadLatest").Visibility == Visibility.Collapsed && Find<Button>(root, "TransferWriteRetry").Visibility == Visibility.Collapsed,
                "request_conflictは版競合や結果不明と混同せず最新採用・同一要求再送を提示しない");
            await InvokeAsync(() => Find<Button>(root, "TransferBack")); await ConfirmTransfer(root, ContentDialogResult.Primary); await Wait(() => TransferEditing is null);
        }
    }
}
